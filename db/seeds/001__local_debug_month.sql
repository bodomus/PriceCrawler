-- Local/dev only.
-- Destructive reset + generate seed script for a realistic month of VARUS-like data.
--
-- Why direct DML instead of full routine-only generation:
-- - this script must backdate runs and queue lifecycle over ~30 days
-- - current run/queue routines stamp timestamps with now()
-- - for historical bulk generation we need explicit control over started_at / finished_at / queue timestamps
-- - for this reason the seed uses direct inserts/updates for historical facts

begin;

set client_min_messages = warning;

do $guard$
begin
    if current_database() <> 'varprice' then
        raise exception 'Seed script is guarded for database "varprice", current database is "%".', current_database();
    end if;
end;
$guard$;

select setseed(0.20260327);

truncate table crawl_error,
               price_snapshot,
               price_collect_queue,
               ingestion_run,
               crawler_run,
               product
    restart identity;

do $seed$
declare
    v_days integer := 30;
    v_runs_per_day integer := 30;
    v_catalog_size integer := 270;
    v_min_snapshots_per_run integer := 50;
    v_max_snapshots_per_run integer := 150;
    v_source text := 'vegetables';
    v_base_names text[] := array[
        'Tomato', 'Cucumber', 'Potato', 'Onion', 'Garlic', 'Carrot', 'Cabbage', 'Beetroot', 'Pepper',
        'Eggplant', 'Zucchini', 'Pumpkin', 'Broccoli', 'Cauliflower', 'Lettuce', 'Dill', 'Parsley',
        'Spinach', 'Celery', 'Radish', 'Avocado', 'Banana', 'Apple', 'Pear', 'Orange', 'Lemon', 'Lime',
        'Mandarin', 'Grapes', 'Kiwi', 'Mango', 'Pineapple', 'Plum', 'Peach', 'Nectarine', 'Apricot',
        'Pomegranate', 'Grapefruit', 'Cherry', 'Strawberry', 'Blueberry', 'Raspberry', 'Blackberry',
        'Watermelon', 'Melon'
    ];
    v_variants text[] := array['Fresh', 'Farm', 'Premium', 'Selected', 'Organic', 'Daily'];
    v_day_index integer;
    v_run_index integer;
    v_snapshot_index integer;
    v_dead_index integer;
    v_current_day timestamptz;
    v_catalog_created_at timestamptz;
    v_run_id bigint;
    v_ingestion_run_id bigint;
    v_run_started_at timestamptz;
    v_run_finished_at timestamptz;
    v_run_duration_seconds integer;
    v_run_spacing_minutes integer;
    v_snapshot_target integer;
    v_failed_snapshot_target integer;
    v_dead_error_target integer;
    v_retry_success_target integer;
    v_failed_snapshot_count integer;
    v_dead_error_count integer;
    v_snapshot_count integer;
    v_clean_snapshot_count integer;
    v_retry_success_count integer;
    v_outstanding_retry_count integer;
    v_outstanding_reserved_count integer;
    v_outstanding_pending_count integer;
    v_worker_id text;
    v_note text;
    v_run_status text;
    v_error_code text;
    v_error_message text;
    v_http_status integer;
    v_queue_id bigint;
    v_queue_created_at timestamptz;
    v_queue_reserved_at timestamptz;
    v_queue_finished_at timestamptz;
    v_observed_at timestamptz;
    v_attempt integer;
    v_max_attempts integer := 3;
    v_name text;
    v_slug text;
    v_price numeric(18, 2);
    v_old_price numeric(18, 2);
    v_regular_price numeric(18, 2);
    v_discount numeric(5, 2);
    v_promo_flag boolean;
    v_in_stock boolean;
    v_seed record;
begin
    v_run_spacing_minutes := greatest(5, floor(840::numeric / greatest(v_runs_per_day, 1))::integer);

    create temporary table tmp_seed_catalog
    (
        seed_id        integer generated always as identity primary key,
        external_id    varchar(64)   not null,
        name           varchar(512)  not null,
        url            varchar(1024) not null,
        slug           varchar(512)  not null,
        pack_value     numeric(18, 6),
        pack_unit      varchar(16),
        regular_price  numeric(18, 2) not null,
        promo_flag     boolean        not null default false,
        promo_discount numeric(5, 2),
        in_stock       boolean        not null default true,
        promo_bias     numeric(5, 2) not null,
        stockout_bias  numeric(5, 2) not null,
        product_id     bigint
    ) on commit drop;

    insert into tmp_seed_catalog(
        external_id,
        name,
        url,
        slug,
        pack_value,
        pack_unit,
        regular_price,
        promo_flag,
        promo_discount,
        in_stock,
        promo_bias,
        stockout_bias)
    select
        'SKU-' || lpad(gs.n::text, 5, '0'),
        naming.base_name || ' ' || naming.variant,
        'https://varus.ua/products/' || seed.slug,
        seed.slug,
        seed.pack_value,
        seed.pack_unit,
        seed.regular_price,
        false,
        null,
        true,
        seed.promo_bias,
        seed.stockout_bias
    from generate_series(1, v_catalog_size) as gs(n)
    cross join lateral (
        select
            v_base_names[1 + mod(gs.n - 1, array_length(v_base_names, 1))] as base_name,
            v_variants[1 + mod(((gs.n - 1) / array_length(v_base_names, 1))::integer, array_length(v_variants, 1))] as variant
    ) as naming
    cross join lateral (
        select
            lower(regexp_replace(naming.base_name || '-' || naming.variant || '-' || lpad(gs.n::text, 4, '0'), '[^a-z0-9]+', '-', 'g')) as slug,
            case mod(gs.n - 1, 4)
                when 0 then array[0.500::numeric, 1.000::numeric, 1.500::numeric, 2.000::numeric][1 + mod(gs.n - 1, 4)]
                when 1 then array[1.000::numeric, 2.000::numeric, 4.000::numeric, 6.000::numeric][1 + mod(gs.n - 1, 4)]
                when 2 then array[250.000::numeric, 400.000::numeric, 500.000::numeric, 750.000::numeric][1 + mod(gs.n - 1, 4)]
                else 1.000::numeric
            end as pack_value,
            case mod(gs.n - 1, 4)
                when 0 then 'kg'
                when 1 then 'pcs'
                when 2 then 'g'
                else 'pack'
            end as pack_unit,
            round(
                (
                    case
                        when naming.base_name in ('Avocado', 'Blueberry', 'Blackberry', 'Raspberry', 'Cherry', 'Mango', 'Pomegranate') then 90 + random() * 160
                        when naming.base_name in ('Banana', 'Apple', 'Pear', 'Orange', 'Kiwi', 'Mandarin', 'Peach', 'Nectarine', 'Apricot', 'Plum') then 28 + random() * 80
                        when naming.base_name in ('Watermelon', 'Melon', 'Pineapple', 'Grapefruit') then 35 + random() * 95
                        else 18 + random() * 65
                    end
                )::numeric,
                2) as regular_price,
            round((0.07 + random() * 0.12)::numeric, 2) as promo_bias,
            round((0.02 + random() * 0.05)::numeric, 2) as stockout_bias
    ) as seed;

    v_catalog_created_at := date_trunc('day', now()) - make_interval(days => v_days + 7);

    with inserted as (
        insert into product(
            external_id,
            name,
            url,
            slug,
            pack_value,
            pack_unit,
            created_at,
            updated_at)
        select
            external_id,
            name,
            url,
            slug,
            pack_value,
            pack_unit,
            v_catalog_created_at,
            v_catalog_created_at
        from tmp_seed_catalog
        returning id, external_id
    )
    update tmp_seed_catalog as seed
    set product_id = inserted.id
    from inserted
    where inserted.external_id = seed.external_id;

    for v_day_index in 0..(v_days - 1) loop
        v_current_day := date_trunc('day', now()) - make_interval(days => (v_days - 1 - v_day_index));

        for v_run_index in 1..v_runs_per_day loop
            v_worker_id := format('seed-worker-%s', 1 + mod(v_run_index - 1, 6));
            v_run_duration_seconds := 240 + floor(random() * 720)::integer;
            v_run_started_at := v_current_day
                + interval '06:00'
                + make_interval(mins => ((v_run_index - 1) * v_run_spacing_minutes) + floor(random() * 10)::integer);
            v_run_finished_at := v_run_started_at + make_interval(secs => v_run_duration_seconds);

            insert into crawler_run(started_at, finished_at, status, source, note)
            values (v_run_started_at, v_run_finished_at, 'running', v_source, null)
            returning id into v_run_id;

            insert into ingestion_run(crawler_run_id, started_at, finished_at, status, error_code, error_message)
            values (v_run_id, v_run_started_at + interval '5 seconds', null, 'running', null, null)
            returning ingestion_run_id into v_ingestion_run_id;

            v_snapshot_target := v_min_snapshots_per_run + floor(random() * (v_max_snapshots_per_run - v_min_snapshots_per_run + 1))::integer;
            v_failed_snapshot_target := greatest(2, round(v_snapshot_target * (0.05 + random() * 0.05))::integer);
            v_dead_error_target := greatest(1, round(v_snapshot_target * (0.02 + random() * 0.02))::integer);
            v_retry_success_target := greatest(2, round(v_snapshot_target * (0.10 + random() * 0.08))::integer);

            v_failed_snapshot_count := 0;
            v_dead_error_count := 0;
            v_snapshot_count := 0;
            v_clean_snapshot_count := 0;
            v_retry_success_count := 0;

            for v_seed in
                select *
                from tmp_seed_catalog
                order by random()
                limit v_snapshot_target
            loop
                v_snapshot_index := v_snapshot_count + 1;
                v_queue_created_at := v_run_started_at + make_interval(secs => least(v_run_duration_seconds - 20, (v_snapshot_index * greatest(v_run_duration_seconds / greatest(v_snapshot_target, 1), 1))));
                v_queue_reserved_at := v_queue_created_at + make_interval(secs => 2 + floor(random() * 10)::integer);
                v_queue_finished_at := least(v_run_finished_at, v_queue_reserved_at + make_interval(secs => 10 + floor(random() * 45)::integer));
                v_observed_at := v_queue_finished_at - make_interval(secs => floor(random() * 8)::integer);

                v_attempt := case
                    when v_retry_success_count < v_retry_success_target and random() < 0.80 then 1 + floor(random() * 2)::integer
                    else 0
                end;

                v_regular_price := v_seed.regular_price;
                if random() < 0.72 then
                    v_regular_price := round(greatest(7, v_regular_price * (1 + ((random() - 0.5) * 0.14)))::numeric, 2);
                end if;

                if v_failed_snapshot_count < v_failed_snapshot_target then
                    if coalesce(v_seed.promo_flag, false) and random() < 0.62 then
                        v_promo_flag := true;
                    else
                        v_promo_flag := random() < greatest(v_seed.promo_bias, 0.10);
                    end if;
                else
                    if coalesce(v_seed.promo_flag, false) and random() < 0.58 then
                        v_promo_flag := true;
                    else
                        v_promo_flag := random() < v_seed.promo_bias;
                    end if;
                end if;

                if coalesce(v_seed.in_stock, true) then
                    v_in_stock := random() >= v_seed.stockout_bias;
                else
                    v_in_stock := random() < 0.58;
                end if;

                if v_promo_flag then
                    v_discount := round((0.10 + random() * 0.22)::numeric, 2);
                    v_old_price := v_regular_price;
                    v_price := round((v_regular_price * (1 - v_discount))::numeric, 2);
                else
                    v_discount := null;
                    v_old_price := null;
                    v_price := v_regular_price;
                end if;

                if not v_in_stock and v_price is null and v_old_price is null then
                    v_old_price := v_regular_price;
                end if;

                if v_failed_snapshot_count < v_failed_snapshot_target and random() < 0.82 then
                    if not v_promo_flag then
                        v_promo_flag := true;
                        v_discount := round((0.12 + random() * 0.18)::numeric, 2);
                        v_old_price := v_regular_price;
                        v_price := round((v_regular_price * (1 - v_discount))::numeric, 2);
                    else
                        v_discount := least(coalesce(v_discount, 0.14) + 0.05, 0.35);
                        v_old_price := v_regular_price;
                        v_price := round((v_regular_price * (1 - v_discount))::numeric, 2);
                    end if;
                end if;

                v_name := v_seed.name;
                v_slug := v_seed.slug;

                insert into price_collect_queue(
                    run_id,
                    url,
                    status,
                    attempt,
                    max_attempts,
                    next_attempt_at,
                    reserved_at,
                    lease_until,
                    reserved_by,
                    idempotency_key,
                    last_error_code,
                    last_http_status,
                    last_error_message,
                    created_at,
                    updated_at,
                    finished_at)
                values (
                    v_run_id,
                    v_seed.url,
                    'succeeded',
                    v_attempt,
                    v_max_attempts,
                    v_queue_created_at,
                    null,
                    null,
                    null,
                    format('seed-%s-%s', v_run_id, v_seed.seed_id),
                    case when v_attempt > 0 then (array['timeout', 'too_many_requests', 'http_5xx'])[1 + floor(random() * 3)::integer] else null end,
                    case when v_attempt > 0 then (array[504, 429, 502])[1 + floor(random() * 3)::integer] else null end,
                    case when v_attempt > 0 then 'Recovered after transient upstream error' else null end,
                    v_queue_created_at,
                    v_queue_finished_at,
                    v_queue_finished_at)
                returning id into v_queue_id;

                update product
                set name = v_name,
                    slug = v_slug,
                    pack_value = v_seed.pack_value,
                    pack_unit = v_seed.pack_unit,
                    updated_at = v_observed_at
                where id = v_seed.product_id;

                insert into price_snapshot(
                    run_id,
                    product_id,
                    captured_at,
                    price,
                    old_price,
                    promo_flag,
                    in_stock,
                    queue_id)
                values (
                    v_run_id,
                    v_seed.product_id,
                    v_observed_at,
                    v_price,
                    v_old_price,
                    v_promo_flag,
                    v_in_stock,
                    v_queue_id);

                v_snapshot_count := v_snapshot_count + 1;

                if v_attempt > 0 then
                    v_retry_success_count := v_retry_success_count + 1;
                else
                    v_clean_snapshot_count := v_clean_snapshot_count + 1;
                end if;

                if v_failed_snapshot_count < v_failed_snapshot_target and random() < 0.82 then
                    if random() < 0.30 then
                        v_error_code := 'timeout';
                        v_http_status := 504;
                        v_error_message := 'Request timed out after partial product payload was received.';
                    elsif random() < 0.60 then
                        v_error_code := 'too_many_requests';
                        v_http_status := 429;
                        v_error_message := 'Upstream throttled request but partial card data was still extracted.';
                    elsif random() < 0.85 then
                        v_error_code := 'parse_failed';
                        v_http_status := null;
                        v_error_message := 'HTML structure changed and some fields required fallback parsing.';
                    else
                        v_error_code := 'http_5xx';
                        v_http_status := 502;
                        v_error_message := 'Upstream returned 5xx after the main card payload had already been parsed.';
                    end if;

                    insert into crawl_error(
                        run_id,
                        queue_id,
                        product_id,
                        url,
                        error_code,
                        http_status,
                        error_message,
                        created_at)
                    values (
                        v_run_id,
                        v_queue_id,
                        v_seed.product_id,
                        v_seed.url,
                        v_error_code,
                        v_http_status,
                        v_error_message,
                        v_observed_at + interval '3 seconds');

                    v_failed_snapshot_count := v_failed_snapshot_count + 1;
                end if;

                update tmp_seed_catalog
                set regular_price = v_regular_price,
                    promo_flag = v_promo_flag,
                    promo_discount = v_discount,
                    in_stock = v_in_stock
                where seed_id = v_seed.seed_id;
            end loop;

            for v_seed in
                select *
                from tmp_seed_catalog
                order by random()
                limit v_dead_error_target
            loop
                v_dead_index := v_dead_error_count + 1;
                v_queue_created_at := v_run_started_at + make_interval(secs => least(v_run_duration_seconds - 20, floor(random() * greatest(v_run_duration_seconds - 20, 20))::integer));
                v_queue_finished_at := v_queue_created_at + make_interval(secs => 8 + floor(random() * 35)::integer);

                if random() < 0.30 then
                    v_error_code := 'not_found';
                    v_http_status := 404;
                    v_error_message := 'Product page returned 404 during seed run.';
                elsif random() < 0.55 then
                    v_error_code := 'timeout';
                    v_http_status := 504;
                    v_error_message := 'Request timed out before a valid card payload was collected.';
                elsif random() < 0.78 then
                    v_error_code := 'too_many_requests';
                    v_http_status := 429;
                    v_error_message := 'Upstream throttled the request and no valid observation was recorded.';
                elsif random() < 0.92 then
                    v_error_code := 'http_5xx';
                    v_http_status := 503;
                    v_error_message := 'VARUS upstream responded with 5xx and no snapshot was stored.';
                else
                    v_error_code := 'parse_failed';
                    v_http_status := null;
                    v_error_message := 'Parser could not build a valid card from the fetched HTML.';
                end if;

                insert into price_collect_queue(
                    run_id,
                    url,
                    status,
                    attempt,
                    max_attempts,
                    next_attempt_at,
                    reserved_at,
                    lease_until,
                    reserved_by,
                    idempotency_key,
                    last_error_code,
                    last_http_status,
                    last_error_message,
                    created_at,
                    updated_at,
                    finished_at)
                values (
                    v_run_id,
                    v_seed.url,
                    'dead',
                    1 + floor(random() * 2)::integer,
                    v_max_attempts,
                    v_queue_created_at,
                    null,
                    null,
                    null,
                    format('seed-dead-%s-%s-%s', v_run_id, v_seed.seed_id, v_dead_index),
                    v_error_code,
                    v_http_status,
                    v_error_message,
                    v_queue_created_at,
                    v_queue_finished_at,
                    v_queue_finished_at)
                returning id into v_queue_id;

                insert into crawl_error(
                    run_id,
                    queue_id,
                    product_id,
                    url,
                    error_code,
                    http_status,
                    error_message,
                    created_at)
                values (
                    v_run_id,
                    v_queue_id,
                    v_seed.product_id,
                    v_seed.url,
                    v_error_code,
                    v_http_status,
                    v_error_message,
                    v_queue_finished_at);

                v_dead_error_count := v_dead_error_count + 1;
            end loop;

            v_run_status := case
                when (v_failed_snapshot_count + v_dead_error_count) >= greatest(6, floor(v_snapshot_target * 0.10)::integer) and random() < 0.70 then 'error'
                when random() < 0.08 then 'error'
                else 'ok'
            end;

            v_note := format(
                'seed: snapshots=%s, failedSnapshots=%s, deadErrors=%s, retrySuccess=%s',
                v_snapshot_count,
                v_failed_snapshot_count,
                v_dead_error_count,
                v_retry_success_count);

            update crawler_run
            set status = v_run_status,
                finished_at = v_run_finished_at,
                note = left(v_note, 255)
            where id = v_run_id;

            update ingestion_run
            set status = v_run_status,
                finished_at = v_run_finished_at - interval '2 seconds',
                error_code = case when v_run_status = 'error' then 'seed_run_has_failures' else null end,
                error_message = case when v_run_status = 'error' then left(v_note, 512) else null end
            where ingestion_run_id = v_ingestion_run_id;
        end loop;
    end loop;

    v_current_day := date_trunc('day', now());
    v_run_started_at := now() - interval '35 minutes';

    insert into crawler_run(started_at, finished_at, status, source, note)
    values (v_run_started_at, null, 'running', v_source, 'seed: active run with outstanding queue items')
    returning id into v_run_id;

    insert into ingestion_run(crawler_run_id, started_at, finished_at, status, error_code, error_message)
    values (v_run_id, v_run_started_at + interval '5 seconds', null, 'running', null, null)
    returning ingestion_run_id into v_ingestion_run_id;

    v_outstanding_retry_count := 0;
    v_outstanding_reserved_count := 0;
    v_outstanding_pending_count := 0;
    v_snapshot_count := 0;
    v_failed_snapshot_count := 0;

    for v_seed in
        select *
        from tmp_seed_catalog
        order by random()
        limit 18
    loop
        v_queue_created_at := v_run_started_at + make_interval(secs => floor(random() * 900)::integer);
        v_queue_reserved_at := v_queue_created_at + make_interval(secs => 5 + floor(random() * 15)::integer);
        v_observed_at := least(now() - interval '2 minutes', v_queue_reserved_at + make_interval(secs => 20 + floor(random() * 40)::integer));
        v_regular_price := round(greatest(7, v_seed.regular_price * (1 + ((random() - 0.5) * 0.08)))::numeric, 2);
        v_promo_flag := random() < greatest(v_seed.promo_bias, 0.10);
        v_in_stock := random() >= v_seed.stockout_bias;

        if v_promo_flag then
            v_discount := round((0.10 + random() * 0.18)::numeric, 2);
            v_old_price := v_regular_price;
            v_price := round((v_regular_price * (1 - v_discount))::numeric, 2);
        else
            v_discount := null;
            v_old_price := null;
            v_price := v_regular_price;
        end if;

        insert into price_collect_queue(
            run_id,
            url,
            status,
            attempt,
            max_attempts,
            next_attempt_at,
            reserved_at,
            lease_until,
            reserved_by,
            idempotency_key,
            last_error_code,
            last_http_status,
            last_error_message,
            created_at,
            updated_at,
            finished_at)
        values (
            v_run_id,
            v_seed.url,
            'succeeded',
            case when random() < 0.25 then 1 else 0 end,
            v_max_attempts,
            v_queue_created_at,
            null,
            null,
            null,
            format('seed-running-success-%s-%s', v_run_id, v_seed.seed_id),
            null,
            null,
            null,
            v_queue_created_at,
            v_observed_at,
            v_observed_at)
        returning id into v_queue_id;

        update product
        set updated_at = v_observed_at
        where id = v_seed.product_id;

        insert into price_snapshot(
            run_id,
            product_id,
            captured_at,
            price,
            old_price,
            promo_flag,
            in_stock,
            queue_id)
        values (
            v_run_id,
            v_seed.product_id,
            v_observed_at,
            v_price,
            v_old_price,
            v_promo_flag,
            v_in_stock,
            v_queue_id);

        v_snapshot_count := v_snapshot_count + 1;

        if random() < 0.20 then
            insert into crawl_error(
                run_id,
                queue_id,
                product_id,
                url,
                error_code,
                http_status,
                error_message,
                created_at)
            values (
                v_run_id,
                v_queue_id,
                v_seed.product_id,
                v_seed.url,
                'parse_failed',
                null,
                'Active seed run captured partial parsing warning for this product.',
                v_observed_at + interval '2 seconds');

            v_failed_snapshot_count := v_failed_snapshot_count + 1;
        end if;

        update tmp_seed_catalog
        set regular_price = v_regular_price,
            promo_flag = v_promo_flag,
            promo_discount = v_discount,
            in_stock = v_in_stock
        where seed_id = v_seed.seed_id;
    end loop;

    for v_seed in
        select *
        from tmp_seed_catalog
        order by random()
        limit 4
    loop
        v_queue_created_at := now() - make_interval(mins => 10 + floor(random() * 15)::integer);

        insert into price_collect_queue(
            run_id,
            url,
            status,
            attempt,
            max_attempts,
            next_attempt_at,
            reserved_at,
            lease_until,
            reserved_by,
            idempotency_key,
            last_error_code,
            last_http_status,
            last_error_message,
            created_at,
            updated_at,
            finished_at)
        values (
            v_run_id,
            v_seed.url,
            'retry',
            1,
            v_max_attempts,
            now() + make_interval(mins => 2 + floor(random() * 8)::integer),
            null,
            null,
            null,
            format('seed-running-retry-%s-%s', v_run_id, v_seed.seed_id),
            'timeout',
            504,
            'Transient timeout, queue item left in retry for active seed run.',
            v_queue_created_at,
            now() - make_interval(mins => 1 + floor(random() * 3)::integer),
            null);

        v_outstanding_retry_count := v_outstanding_retry_count + 1;
    end loop;

    for v_seed in
        select *
        from tmp_seed_catalog
        order by random()
        limit 3
    loop
        v_queue_created_at := now() - make_interval(mins => 2 + floor(random() * 6)::integer);

        insert into price_collect_queue(
            run_id,
            url,
            status,
            attempt,
            max_attempts,
            next_attempt_at,
            reserved_at,
            lease_until,
            reserved_by,
            idempotency_key,
            last_error_code,
            last_http_status,
            last_error_message,
            created_at,
            updated_at,
            finished_at)
        values (
            v_run_id,
            v_seed.url,
            'reserved',
            0,
            v_max_attempts,
            v_queue_created_at,
            v_queue_created_at + interval '5 seconds',
            now() + interval '90 seconds',
            'seed-worker-live',
            format('seed-running-reserved-%s-%s', v_run_id, v_seed.seed_id),
            null,
            null,
            null,
            v_queue_created_at,
            now(),
            null);

        v_outstanding_reserved_count := v_outstanding_reserved_count + 1;
    end loop;

    for v_seed in
        select *
        from tmp_seed_catalog
        order by random()
        limit 5
    loop
        v_queue_created_at := now() - make_interval(secs => floor(random() * 180)::integer);

        insert into price_collect_queue(
            run_id,
            url,
            status,
            attempt,
            max_attempts,
            next_attempt_at,
            reserved_at,
            lease_until,
            reserved_by,
            idempotency_key,
            last_error_code,
            last_http_status,
            last_error_message,
            created_at,
            updated_at,
            finished_at)
        values (
            v_run_id,
            v_seed.url,
            'pending',
            0,
            v_max_attempts,
            now() + make_interval(secs => 30 + floor(random() * 120)::integer),
            null,
            null,
            null,
            format('seed-running-pending-%s-%s', v_run_id, v_seed.seed_id),
            null,
            null,
            null,
            v_queue_created_at,
            v_queue_created_at,
            null);

        v_outstanding_pending_count := v_outstanding_pending_count + 1;
    end loop;

    v_note := format(
        'seed: snapshots=%s, failedSnapshots=%s, retry=%s, reserved=%s, pending=%s',
        v_snapshot_count,
        v_failed_snapshot_count,
        v_outstanding_retry_count,
        v_outstanding_reserved_count,
        v_outstanding_pending_count);

    update crawler_run
    set note = left(v_note, 255)
    where id = v_run_id;

    raise notice 'Seed generation completed: runs=%, products=%, snapshots=%, queue=%, errors=%',
        (select count(*) from crawler_run),
        (select count(*) from product),
        (select count(*) from price_snapshot),
        (select count(*) from price_collect_queue),
        (select count(*) from crawl_error);
end;
$seed$;

commit;

select 'crawler_run' as metric, count(*)::bigint as total from crawler_run
union all
select 'ingestion_run', count(*)::bigint from ingestion_run
union all
select 'product', count(*)::bigint from product
union all
select 'price_snapshot', count(*)::bigint from price_snapshot
union all
select 'price_collect_queue', count(*)::bigint from price_collect_queue
union all
select 'crawl_error', count(*)::bigint from crawl_error
order by metric;

select status, count(*)::bigint as total
from crawler_run
group by status
order by status;

select status, count(*)::bigint as total
from price_collect_queue
group by status
order by status;
