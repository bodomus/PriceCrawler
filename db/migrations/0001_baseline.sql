
-- PriceCrawler schema version 1 baseline.
-- Creates a new empty database at the v0.4.1-alpha schema level.
-- This migration is forward-only and must never be used as a repair script.

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class object
        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = object.relnamespace
        WHERE namespace.nspname = 'public'
          AND object.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
    ) OR EXISTS (
        SELECT 1
        FROM pg_catalog.pg_proc routine
        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
        WHERE namespace.nspname = 'public'
    ) THEN
        RAISE EXCEPTION USING
            MESSAGE = 'PriceCrawler baseline requires an empty public schema.',
            HINT = 'Use db/scripts/bootstrap-schema-version.sql for an existing compatible database.';
    END IF;
END;
$$;

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

CREATE FUNCTION public.crawl_error_add(p_run_id bigint, p_queue_id bigint, p_product_id bigint, p_url text, p_created_at timestamp with time zone, p_error_code text, p_http_status integer, p_error_message text) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
declare
v_id bigint;
begin
insert into crawl_error(run_id,
                        queue_id,
                        product_id,
                        url,
                        error_code,
                        http_status,
                        error_message,
                        created_at)
values (p_run_id,
        p_queue_id,
        p_product_id,
        routine_support_trim_nullable(p_url, 1024),
        coalesce(
            lower(routine_support_trim_nullable(p_error_code, 64)),
            'unknown'),
        p_http_status,
        routine_support_trim_nullable(p_error_message, 512),
        coalesce(p_created_at, now())) returning id
into v_id;

return v_id;
end;
$$;

CREATE PROCEDURE public.crawler_run_complete(IN p_run_id bigint, IN p_status text, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_reactivated_count integer, IN p_deactivated_count integer, IN p_selected_count integer, IN p_enqueued_count integer, IN p_succeeded_count integer, IN p_retry_count integer, IN p_dead_count integer, IN p_failed_count integer, IN p_products_created_count integer, IN p_products_updated_count integer, IN p_snapshots_created_count integer, IN p_errors_created_count integer, IN p_stages_json text, IN p_note text DEFAULT NULL::text, IN p_error_code text DEFAULT NULL::text, IN p_error_message text DEFAULT NULL::text)
    LANGUAGE plpgsql
    AS $$
declare
v_finished_at timestamptz := now();
v_completion_started_at
timestamptz := clock_timestamp();
v_db_finalization_ms
bigint;
begin
update crawler_run
set status                  = routine_support_run_status(p_status),
    finished_at             = v_finished_at,
    duration_ms             = greatest(0, floor(extract(epoch from (v_finished_at - started_at)) * 1000)::bigint),
    discovered_count        = greatest(coalesce(p_discovered_count, 0), 0),
    accepted_count          = greatest(coalesce(p_accepted_count, 0), 0),
    inserted_count          = greatest(coalesce(p_inserted_count, 0), 0),
    updated_count           = greatest(coalesce(p_updated_count, 0), 0),
    reactivated_count       = greatest(coalesce(p_reactivated_count, 0), 0),
    deactivated_count       = greatest(coalesce(p_deactivated_count, 0), 0),
    selected_count          = greatest(coalesce(p_selected_count, 0), 0),
    enqueued_count          = greatest(coalesce(p_enqueued_count, 0), 0),
    succeeded_count         = greatest(coalesce(p_succeeded_count, 0), 0),
    retry_count             = greatest(coalesce(p_retry_count, 0), 0),
    dead_count              = greatest(coalesce(p_dead_count, 0), 0),
    failed_count            = greatest(coalesce(p_failed_count, 0), 0),
    products_created_count  = greatest(coalesce(p_products_created_count, 0), 0),
    products_updated_count  = greatest(coalesce(p_products_updated_count, 0), 0),
    snapshots_created_count = greatest(coalesce(p_snapshots_created_count, 0), 0),
    errors_created_count    = greatest(coalesce(p_errors_created_count, 0), 0),
    note                    = routine_support_trim_nullable(p_note, 255),
    error_code              = routine_support_trim_nullable(p_error_code, 100),
    error_message           = routine_support_trim_nullable(p_error_message, 1000),
    updated_at              = v_finished_at
where id = p_run_id;

if
not found then raise exception 'crawler_run % was not found.', p_run_id;
end if;

delete
from crawler_run_stage
where run_id = p_run_id;

v_db_finalization_ms
:= greatest(
    0,
    floor(extract(epoch from (clock_timestamp() - v_completion_started_at)) * 1000)::bigint);

insert into crawler_run_stage(run_id, stage, started_at, finished_at, duration_ms, item_count)
select p_run_id,
       routine_support_trim_required(x.stage, 100),
       v_finished_at - make_interval(secs => timing.effective_duration_ms::double precision / 1000.0),
       v_finished_at,
       timing.effective_duration_ms,
       x.item_count
from jsonb_to_recordset(coalesce(nullif(p_stages_json, ''), '[]')::jsonb)
         as x(stage text, duration_ms bigint, item_count integer)
         cross join lateral (
    select greatest(x.duration_ms, 0)
               + case when x.stage = 'run-finalization' then v_db_finalization_ms else 0 end
               as effective_duration_ms) timing;
end $$;

CREATE PROCEDURE public.crawler_run_finish(IN p_run_id bigint, IN p_status text, IN p_note text DEFAULT NULL::text)
    LANGUAGE plpgsql
    AS $$
begin
update crawler_run
set status      = routine_support_run_status(p_status),
    note        = routine_support_trim_nullable(p_note, 255),
    finished_at = now()
where id = p_run_id;
end;
$$;

CREATE FUNCTION public.crawler_run_get_aggregate(p_from timestamp with time zone, p_to timestamp with time zone, p_run_type text) RETURNS TABLE(total_runs integer, successful_runs integer, failed_runs integer, total_duration_ms bigint, average_duration_ms double precision, total_discovered bigint, total_accepted bigint, total_selected bigint, total_succeeded bigint, total_dead bigint, total_snapshots_created bigint, total_errors_created bigint)
    LANGUAGE sql STABLE
    AS $$
select count(*)::integer, count(*) filter(where status = 'ok')::integer, count(*) filter(where status = 'error')::integer, coalesce(sum(duration_ms), 0)::bigint, coalesce(avg(duration_ms), 0) ::double precision, coalesce(sum(discovered_count), 0)::bigint,
           coalesce(sum(accepted_count), 0)::bigint, coalesce(sum(selected_count), 0)::bigint,
           coalesce(sum(succeeded_count), 0)::bigint, coalesce(sum(dead_count), 0)::bigint,
           coalesce(sum(snapshots_created_count), 0)::bigint, coalesce(sum(errors_created_count), 0)::bigint
from crawler_run
where started_at >= p_from
  and started_at
    < p_to
  and (nullif (btrim(p_run_type)
    , '') is null
   or run_type = lower (btrim(p_run_type)));
$$;

CREATE FUNCTION public.crawler_run_get_by_id(p_run_id bigint) RETURNS TABLE(id bigint, run_type character varying, source character varying, discovery_source character varying, status character varying, started_at timestamp with time zone, finished_at timestamp with time zone, duration_ms bigint, discovered_count integer, accepted_count integer, inserted_count integer, updated_count integer, reactivated_count integer, deactivated_count integer, selected_count integer, enqueued_count integer, succeeded_count integer, retry_count integer, dead_count integer, failed_count integer, products_created_count integer, products_updated_count integer, snapshots_created_count integer, errors_created_count integer, error_code character varying, error_message character varying, note character varying)
    LANGUAGE sql STABLE
    AS $$
select cr.id,
       cr.run_type,
       cr.source,
       cr.discovery_source,
       cr.status,
       cr.started_at,
       cr.finished_at,
       cr.duration_ms,
       cr.discovered_count,
       cr.accepted_count,
       cr.inserted_count,
       cr.updated_count,
       cr.reactivated_count,
       cr.deactivated_count,
       cr.selected_count,
       cr.enqueued_count,
       cr.succeeded_count,
       cr.retry_count,
       cr.dead_count,
       cr.failed_count,
       cr.products_created_count,
       cr.products_updated_count,
       cr.snapshots_created_count,
       cr.errors_created_count,
       cr.error_code,
       cr.error_message,
       cr.note
from crawler_run cr
where cr.id = p_run_id;
$$;

CREATE FUNCTION public.crawler_run_get_recent(p_limit integer, p_run_type text, p_status text) RETURNS TABLE(id bigint, run_type character varying, source character varying, status character varying, started_at timestamp with time zone, finished_at timestamp with time zone, duration_ms bigint, primary_count integer, succeeded_count integer, failed_count integer, error_code character varying)
    LANGUAGE sql STABLE
    AS $$
select r.id,
       r.run_type,
       r.source,
       r.status,
       r.started_at,
       r.finished_at,
       r.duration_ms,
       case r.run_type
           when 'catalog-refresh' then r.accepted_count
           when 'price-collection' then r.selected_count
           else 0 end,
       r.succeeded_count,
       r.failed_count,
       r.error_code
from crawler_run r
where (nullif(btrim(p_run_type), '') is null or r.run_type = lower(btrim(p_run_type)))
  and (nullif(btrim(p_status), '') is null or r.status = lower(btrim(p_status)))
order by r.started_at desc limit least(greatest(coalesce(p_limit, 50), 1), 200);
$$;

CREATE FUNCTION public.crawler_run_stage_get(p_run_id bigint) RETURNS TABLE(stage character varying, duration_ms bigint, item_count integer)
    LANGUAGE sql STABLE
    AS $$
select s.stage, s.duration_ms, s.item_count
from crawler_run_stage s
where s.run_id = p_run_id
order by s.id;
$$;

CREATE FUNCTION public.crawler_run_start(p_source text) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
declare
v_id bigint;
begin
insert into crawler_run(status, source)
values (routine_support_run_status('running'),
        routine_support_trim_required(p_source, 64)) returning id
into v_id;

return v_id;
end;
$$;

CREATE FUNCTION public.crawler_run_start(p_run_type text, p_source text, p_discovery_source text) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
declare
v_id bigint;
begin
insert into crawler_run(status, run_type, source, discovery_source)
values ('running', lower(routine_support_trim_required(p_run_type, 50)),
        routine_support_trim_required(p_source, 64),
        routine_support_trim_nullable(p_discovery_source, 50)) returning id
into v_id;
return v_id;
end $$;

CREATE PROCEDURE public.ingestion_run_finish(IN p_ingestion_run_id bigint, IN p_status text, IN p_error_code text DEFAULT NULL::text, IN p_error_message text DEFAULT NULL::text)
    LANGUAGE plpgsql
    AS $$
begin
update ingestion_run
set status        = routine_support_run_status(p_status),
    error_code    = routine_support_trim_nullable(p_error_code, 128),
    error_message = routine_support_trim_nullable(p_error_message, 512),
    finished_at   = now()
where ingestion_run_id = p_ingestion_run_id;
end;
$$;

CREATE FUNCTION public.ingestion_run_start(p_crawler_run_id bigint) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
declare
v_id bigint;
begin
insert into ingestion_run(crawler_run_id, status)
values (p_crawler_run_id,
        routine_support_run_status('running')) returning ingestion_run_id
into v_id;

return v_id;
end;
$$;

CREATE FUNCTION public.price_collect_queue_enqueue(p_run_id bigint, p_urls text[], p_idempotency_keys text[], p_max_attempts integer, p_product_catalog_ids bigint[] DEFAULT NULL::bigint[], p_page_kinds text[] DEFAULT NULL::text[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
declare
v_count integer;
v_catalog_ids
bigint[];
v_page_kinds
text[];
v_expected
integer;
begin
v_expected
:= coalesce(array_length(p_urls, 1), 0);

if
v_expected <> coalesce(array_length(p_idempotency_keys, 1), 0) then
    raise exception 'p_urls and p_idempotency_keys length mismatch';
end if;

v_catalog_ids
:= coalesce(p_product_catalog_ids, array_fill(null::bigint, array[v_expected]));
v_page_kinds
:= coalesce(p_page_kinds, array_fill('product_page'::text, array[v_expected]));

if
v_expected <> coalesce(array_length(v_catalog_ids, 1), 0) then
    raise exception 'p_urls and p_product_catalog_ids length mismatch';
end if;
if
v_expected <> coalesce(array_length(v_page_kinds, 1), 0) then
    raise exception 'p_urls and p_page_kinds length mismatch';
end if;

with inserted as (
insert
into price_collect_queue(run_id,
                         product_catalog_id,
                         url,
                         page_kind,
                         status,
                         attempt,
                         max_attempts,
                         next_attempt_at,
                         idempotency_key,
                         created_at,
                         updated_at)
select p_run_id,
       x.product_catalog_id,
       routine_support_trim_required(x.url, 1024),
       routine_support_trim_required(coalesce(nullif(btrim(x.page_kind), ''), 'product_page'), 32),
       routine_support_queue_status('pending'),
       0,
       greatest(coalesce(p_max_attempts, 0), 1),
       now(),
       routine_support_trim_required(x.idempotency_key, 128),
       now(),
       now()
from unnest(p_urls, p_idempotency_keys, v_catalog_ids, v_page_kinds) as x(url, idempotency_key, product_catalog_id, page_kind) on conflict (run_id, url) do nothing
        returning 1
    )
select count(*)
into v_count
from inserted;

return coalesce(v_count, 0);
end;
$$;

CREATE FUNCTION public.price_collect_queue_enqueue_result(p_run_id bigint, p_urls text[], p_idempotency_keys text[], p_max_attempts integer, p_product_catalog_ids bigint[] DEFAULT NULL::bigint[], p_page_kinds text[] DEFAULT NULL::text[]) RETURNS TABLE(total_accepted integer, product_accepted integer, listing_accepted integer, accepted_product_catalog_ids bigint[])
    LANGUAGE plpgsql
    AS $$
declare
v_catalog_ids
bigint[];
v_page_kinds
text[];
v_expected
integer;
begin
v_expected
:= coalesce(array_length(p_urls, 1), 0);

if
v_expected <> coalesce(array_length(p_idempotency_keys, 1), 0) then
    raise exception 'p_urls and p_idempotency_keys length mismatch';
end if;

v_catalog_ids
:= coalesce(p_product_catalog_ids, array_fill(null::bigint, array[v_expected]));
v_page_kinds
:= coalesce(p_page_kinds, array_fill('product_page'::text, array[v_expected]));

if
v_expected <> coalesce(array_length(v_catalog_ids, 1), 0) then
    raise exception 'p_urls and p_product_catalog_ids length mismatch';
end if;
if
v_expected <> coalesce(array_length(v_page_kinds, 1), 0) then
    raise exception 'p_urls and p_page_kinds length mismatch';
end if;

return query
with inserted as (
insert
into price_collect_queue(run_id,
                         product_catalog_id,
                         url,
                         page_kind,
                         status,
                         attempt,
                         max_attempts,
                         next_attempt_at,
                         idempotency_key,
                         created_at,
                         updated_at)
select p_run_id,
       x.product_catalog_id,
       routine_support_trim_required(x.url, 1024),
       routine_support_trim_required(coalesce(nullif(btrim(x.page_kind), ''), 'product_page'), 32),
       routine_support_queue_status('pending'),
       0,
       greatest(coalesce(p_max_attempts, 0), 1),
       now(),
       routine_support_trim_required(x.idempotency_key, 128),
       now(),
       now()
from unnest(p_urls, p_idempotency_keys, v_catalog_ids, v_page_kinds) as x(url, idempotency_key, product_catalog_id, page_kind) on conflict (run_id, url) do nothing
        returning price_collect_queue.page_kind, price_collect_queue.product_catalog_id
    )
select count(*)::integer,
       count(*) filter (where inserted.page_kind = 'product_page')::integer,
       count(*) filter (where inserted.page_kind in ('listing_page', 'category_page'))::integer,
       coalesce(array_agg(inserted.product_catalog_id) filter (where inserted.product_catalog_id is not null), array[]::bigint[])
from inserted;
end;
$$;

CREATE FUNCTION public.price_collect_queue_get_run_stats(p_run_id bigint) RETURNS TABLE(pending_count integer, reserved_count integer, retry_count integer, succeeded_count integer, dead_count integer)
    LANGUAGE sql
    AS $$
select count(*) filter (where status = routine_support_queue_status('pending'))::integer as pending_count, count(*) filter (where status = routine_support_queue_status('reserved'))::integer as reserved_count, count(*) filter (where status = routine_support_queue_status('retry'))::integer as retry_count, count(*) filter (where status = routine_support_queue_status('succeeded'))::integer as succeeded_count, count(*) filter (where status = routine_support_queue_status('dead'))::integer as dead_count
from price_collect_queue
where run_id = p_run_id;
$$;

CREATE FUNCTION public.price_collect_queue_has_outstanding(p_run_id bigint) RETURNS boolean
    LANGUAGE sql
    AS $$
select exists (select 1
               from price_collect_queue
               where run_id = p_run_id
                 and status in (
                                routine_support_queue_status('pending'),
                                routine_support_queue_status('retry'),
                                routine_support_queue_status('reserved')));
$$;

CREATE PROCEDURE public.price_collect_queue_mark_dead(IN p_queue_id bigint, IN p_error_code text, IN p_http_status integer, IN p_error_message text)
    LANGUAGE plpgsql
    AS $$
begin
update price_collect_queue
set status             = routine_support_queue_status('dead'),
    attempt            = attempt + 1,
    last_error_code    = routine_support_trim_required(p_error_code, 64),
    last_http_status   = p_http_status,
    last_error_message = routine_support_trim_nullable(p_error_message, 512),
    reserved_at        = null,
    lease_until        = null,
    reserved_by        = null,
    updated_at         = now(),
    finished_at        = now()
where id = p_queue_id;
end;
$$;

CREATE PROCEDURE public.price_collect_queue_mark_retry(IN p_queue_id bigint, IN p_error_code text, IN p_http_status integer, IN p_error_message text, IN p_next_attempt_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
begin
update price_collect_queue
set status             = routine_support_queue_status('retry'),
    attempt            = attempt + 1,
    next_attempt_at    = p_next_attempt_at,
    last_error_code    = routine_support_trim_required(p_error_code, 64),
    last_http_status   = p_http_status,
    last_error_message = routine_support_trim_nullable(p_error_message, 512),
    reserved_at        = null,
    lease_until        = null,
    reserved_by        = null,
    updated_at         = now()
where id = p_queue_id;
end;
$$;

CREATE PROCEDURE public.price_collect_queue_mark_succeeded(IN p_queue_id bigint)
    LANGUAGE plpgsql
    AS $$
begin
update price_collect_queue
set status      = routine_support_queue_status('succeeded'),
    finished_at = now(),
    reserved_at = null,
    lease_until = null,
    reserved_by = null,
    updated_at  = now()
where id = p_queue_id;
end;
$$;

CREATE FUNCTION public.price_collect_queue_reap_expired(p_run_id bigint) RETURNS integer
    LANGUAGE plpgsql
    AS $$
declare
v_count integer;
begin
with updated as (
update price_collect_queue
set status             = routine_support_queue_status('retry'),
    next_attempt_at    = now(),
    reserved_at        = null,
    lease_until        = null,
    reserved_by        = null,
    updated_at         = now(),
    last_error_code    = coalesce(last_error_code, 'lease_expired'),
    last_error_message = coalesce(last_error_message, 'Reservation lease expired')
where run_id = p_run_id
  and status = routine_support_queue_status('reserved')
  and lease_until is not null
  and lease_until < now() returning 1
    )
select count(*)
into v_count
from updated;

return coalesce(v_count, 0);
end;
$$;

CREATE FUNCTION public.price_collect_queue_reserve_batch(p_run_id bigint, p_batch_size integer, p_worker_id text, p_lease_seconds integer) RETURNS TABLE(id bigint, url character varying, attempt integer, max_attempts integer, idempotency_key character varying, product_catalog_id bigint, page_kind character varying)
    LANGUAGE sql
    AS $$
    with candidates as (
        select queue.id
        from price_collect_queue queue
        where queue.run_id = p_run_id
          and queue.status in (
              routine_support_queue_status('pending'),
              routine_support_queue_status('retry'))
          and coalesce(queue.next_attempt_at, queue.created_at, now()) <= now()
        order by coalesce(queue.next_attempt_at, queue.created_at, now()), queue.id
        limit greatest(coalesce(p_batch_size, 0), 1)
        for update skip locked
    ),
    updated as (
        update price_collect_queue queue
        set status = routine_support_queue_status('reserved'),
            reserved_at = now(),
            lease_until = now() + (greatest(coalesce(p_lease_seconds, 0), 1) * interval '1 second'),
            reserved_by = routine_support_trim_required(p_worker_id, 128),
            updated_at = now()
        from candidates
        where queue.id = candidates.id
        returning queue.id, queue.url, queue.attempt, queue.max_attempts, queue.idempotency_key, queue.product_catalog_id, queue.page_kind
    )
select updated.id,
       updated.url,
       updated.attempt,
       updated.max_attempts,
       coalesce(updated.idempotency_key, ''),
       updated.product_catalog_id,
       coalesce(updated.page_kind, 'product_page')
from updated
order by updated.id;
$$;

CREATE FUNCTION public.price_observation_store(p_run_id bigint, p_queue_id bigint, p_external_id text, p_name text, p_url text, p_slug text, p_pack_value numeric, p_pack_unit text, p_price numeric, p_old_price numeric, p_promo_flag boolean, p_in_stock boolean, p_observed_at timestamp with time zone) RETURNS TABLE(product_id bigint, snapshot_id bigint, snapshot_created boolean, product_created boolean, product_updated boolean)
    LANGUAGE plpgsql
    AS $$
declare
v_observed_at timestamptz := coalesce(p_observed_at, now());
    v_external_id
varchar(64) := routine_support_trim_nullable(p_external_id, 64);
    v_name
varchar(512) := routine_support_trim_required(p_name, 512);
    v_url
varchar(1024) := routine_support_trim_required(p_url, 1024);
    v_slug
varchar(512) := routine_support_trim_nullable(p_slug, 512);
    v_pack_unit
varchar(16) := routine_support_trim_nullable(p_pack_unit, 16);
    v_existing_product_id
bigint;
    v_product_id
bigint;
    v_snapshot_id
bigint;
    v_snapshot_created
boolean := false;
    v_product_created
boolean := false;
    v_product_updated
boolean := false;
    v_latest_price
numeric(18, 2);
    v_latest_old_price
numeric(18, 2);
    v_latest_promo_flag
boolean;
    v_latest_in_stock
boolean;
    v_has_minimal_valid_state
boolean := false;
begin
    v_has_minimal_valid_state
:= v_url <> ''
        and (
            p_price is not null
            or p_old_price is not null
            or coalesce(p_in_stock, false));

select product_row.id,
       product_row.external_id is distinct
from coalesce (v_external_id, product_row.external_id)
    or product_row.name is distinct
from v_name
    or product_row.url is distinct
from v_url
    or product_row.slug is distinct
from v_slug
    or product_row.pack_value is distinct
from p_pack_value
    or product_row.pack_unit is distinct
from v_pack_unit
into v_existing_product_id, v_product_updated
from product as product_row
where product_row.url = v_url
   or (v_external_id is not null and product_row.external_id = v_external_id)
order by case when product_row.url = v_url then 0 else 1 end, product_row.id limit 1
    for
update;

v_product_updated
:= coalesce(v_product_updated, false);

if
v_existing_product_id is not null then
update product
set external_id = coalesce(v_external_id, external_id),
    name        = v_name,
    url         = v_url,
    slug        = v_slug,
    pack_value  = p_pack_value,
    pack_unit   = v_pack_unit,
    updated_at  = v_observed_at
where id = v_existing_product_id returning id
into v_product_id;
else
        insert into product(
            external_id,
            name,
            url,
            slug,
            pack_value,
            pack_unit,
            created_at,
            updated_at)
        values(
            v_external_id,
            v_name,
            v_url,
            v_slug,
            p_pack_value,
            v_pack_unit,
            v_observed_at,
            v_observed_at)
    returning id into v_product_id;
    v_product_created
:= true;
end if;

select snapshot_row.id,
       snapshot_row.price,
       snapshot_row.old_price,
       snapshot_row.promo_flag,
       snapshot_row.in_stock
into v_snapshot_id,
    v_latest_price,
    v_latest_old_price,
    v_latest_promo_flag,
    v_latest_in_stock
from price_snapshot as snapshot_row
where snapshot_row.product_id = v_product_id
order by snapshot_row.captured_at desc, snapshot_row.id desc limit 1;

if
v_has_minimal_valid_state then
        if v_snapshot_id is null
           or v_latest_price is distinct from p_price
           or v_latest_old_price is distinct from p_old_price
           or v_latest_promo_flag is distinct from coalesce(p_promo_flag, false)
           or v_latest_in_stock is distinct from coalesce(p_in_stock, false) then
            insert into price_snapshot(
                run_id,
                product_id,
                captured_at,
                price,
                old_price,
                promo_flag,
                in_stock,
                queue_id)
            values(
                p_run_id,
                v_product_id,
                v_observed_at,
                p_price,
                p_old_price,
                coalesce(p_promo_flag, false),
                coalesce(p_in_stock, false),
                p_queue_id)
            returning id into v_snapshot_id;

            v_snapshot_created
:= true;
end if;
end if;

    product_id
:= v_product_id;
    snapshot_id
:= v_snapshot_id;
    snapshot_created
:= v_snapshot_created;
    product_created
:= v_product_created;
    product_updated
:= v_product_updated;
    return
next;
end;
$$;

CREATE FUNCTION public.product_catalog_deactivate_missing(p_source text, p_current_refresh_id bigint, p_not_seen_since timestamp with time zone, p_deactivated_at timestamp with time zone) RETURNS integer
    LANGUAGE plpgsql
    AS $$
declare
v_count integer;
begin
with deactivated as (
update product_catalog catalog
set is_active = false, deactivated_at = p_deactivated_at, next_check_at = null, reserved_at = null, reserved_until = null, reserved_by = null, updated_at = now()
where catalog.source = routine_support_trim_required(p_source
    , 50)
  and catalog.is_active = true
  and coalesce (catalog.last_seen_refresh_id
    , 0) <> p_current_refresh_id
  and catalog.last_discovered_at
    < p_not_seen_since
  and (catalog.reserved_until is null
   or catalog.reserved_until <= p_deactivated_at)
    returning 1
    )
select count(*)
into v_count
from deactivated;

return coalesce(v_count, 0);
end;
$$;

CREATE FUNCTION public.product_catalog_get_active_count(p_source text) RETURNS integer
    LANGUAGE sql
    AS $$
select count(*) ::integer
from product_catalog catalog
where catalog.source = routine_support_trim_required(p_source
    , 50)
  and catalog.is_active = true;
$$;

CREATE FUNCTION public.product_catalog_get_by_id(p_id bigint) RETURNS TABLE(id bigint, source character varying, url character varying, normalized_url character varying, external_id character varying, slug character varying, first_discovered_at timestamp with time zone, last_discovered_at timestamp with time zone, last_checked_at timestamp with time zone, next_check_at timestamp with time zone, is_active boolean, consecutive_errors integer)
    LANGUAGE sql
    AS $$
select catalog.id,
       catalog.source,
       catalog.url,
       catalog.normalized_url,
       catalog.external_id,
       catalog.slug,
       catalog.first_discovered_at,
       catalog.last_discovered_at,
       catalog.last_checked_at,
       catalog.next_check_at,
       catalog.is_active,
       catalog.consecutive_errors
from product_catalog catalog
where catalog.id = p_id;
$$;

CREATE FUNCTION public.product_catalog_get_by_source_normalized_url(p_source text, p_normalized_url text) RETURNS TABLE(id bigint, source character varying, url character varying, normalized_url character varying, external_id character varying, slug character varying, first_discovered_at timestamp with time zone, last_discovered_at timestamp with time zone, last_checked_at timestamp with time zone, next_check_at timestamp with time zone, is_active boolean, consecutive_errors integer)
    LANGUAGE sql
    AS $$
select catalog.id,
       catalog.source,
       catalog.url,
       catalog.normalized_url,
       catalog.external_id,
       catalog.slug,
       catalog.first_discovered_at,
       catalog.last_discovered_at,
       catalog.last_checked_at,
       catalog.next_check_at,
       catalog.is_active,
       catalog.consecutive_errors
from product_catalog catalog
where catalog.source = routine_support_trim_required(p_source
    , 50)
  and catalog.normalized_url = routine_support_trim_required(p_normalized_url
    , 1024);
$$;

CREATE FUNCTION public.product_catalog_get_due(p_limit integer, p_now timestamp with time zone, p_lease_seconds integer, p_worker_id text) RETURNS TABLE(id bigint, source character varying, url character varying, normalized_url character varying, external_id character varying, slug character varying, first_discovered_at timestamp with time zone, last_discovered_at timestamp with time zone, last_checked_at timestamp with time zone, next_check_at timestamp with time zone, is_active boolean, consecutive_errors integer)
    LANGUAGE sql
    AS $$
    with candidates as (
        select catalog.id
        from product_catalog catalog
        where catalog.is_active = true
          and (catalog.next_check_at is null or catalog.next_check_at <= p_now)
          and (catalog.reserved_until is null or catalog.reserved_until <= p_now)
        order by catalog.last_checked_at nulls first,
                 catalog.next_check_at nulls first,
                 catalog.id
        limit greatest(coalesce(p_limit, 0), 1)
        for update skip locked
    ),
    reserved as (
        update product_catalog catalog
        set reserved_at = p_now,
            reserved_until = p_now + (greatest(coalesce(p_lease_seconds, 0), 30) * interval '1 second'),
            reserved_by = routine_support_trim_nullable(p_worker_id, 200),
            updated_at = now()
        from candidates
        where catalog.id = candidates.id
        returning catalog.id,
                  catalog.source,
                  catalog.url,
                  catalog.normalized_url,
                  catalog.external_id,
                  catalog.slug,
                  catalog.first_discovered_at,
                  catalog.last_discovered_at,
                  catalog.last_checked_at,
                  catalog.next_check_at,
                  catalog.is_active,
                  catalog.consecutive_errors
    )
select reserved.id,
       reserved.source,
       reserved.url,
       reserved.normalized_url,
       reserved.external_id,
       reserved.slug,
       reserved.first_discovered_at,
       reserved.last_discovered_at,
       reserved.last_checked_at,
       reserved.next_check_at,
       reserved.is_active,
       reserved.consecutive_errors
from reserved
order by reserved.last_checked_at nulls first,
         reserved.next_check_at nulls first,
         reserved.id;
$$;

CREATE PROCEDURE public.product_catalog_mark_checked(IN p_catalog_item_id bigint, IN p_checked_at timestamp with time zone, IN p_next_check_at timestamp with time zone, IN p_external_id text, IN p_slug text)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog
set last_checked_at    = p_checked_at,
    next_check_at      = p_next_check_at,
    consecutive_errors = 0,
    external_id        = coalesce(nullif(routine_support_trim_nullable(p_external_id, 200), ''), external_id),
    slug               = coalesce(nullif(routine_support_trim_nullable(p_slug, 300), ''), slug),
    reserved_at        = null,
    reserved_until     = null,
    reserved_by        = null,
    updated_at         = now()
where id = p_catalog_item_id;
end;
$$;

CREATE PROCEDURE public.product_catalog_mark_failed(IN p_catalog_item_id bigint, IN p_attempted_at timestamp with time zone, IN p_next_check_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog
set last_checked_at    = p_attempted_at,
    next_check_at      = p_next_check_at,
    consecutive_errors = consecutive_errors + 1,
    reserved_at        = null,
    reserved_until     = null,
    reserved_by        = null,
    updated_at         = now()
where id = p_catalog_item_id;
end;
$$;

CREATE PROCEDURE public.product_catalog_refresh_complete(IN p_refresh_id bigint, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_deactivated_count integer, IN p_reactivated_count integer, IN p_finished_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog_refresh
set finished_at       = p_finished_at,
    status            = 'ok',
    discovered_count  = greatest(coalesce(p_discovered_count, 0), 0),
    accepted_count    = greatest(coalesce(p_accepted_count, 0), 0),
    inserted_count    = greatest(coalesce(p_inserted_count, 0), 0),
    updated_count     = greatest(coalesce(p_updated_count, 0), 0),
    deactivated_count = greatest(coalesce(p_deactivated_count, 0), 0),
    reactivated_count = greatest(coalesce(p_reactivated_count, 0), 0),
    error_code        = null,
    error_message     = null,
    updated_at        = now()
where id = p_refresh_id
  and status = 'running';
end;
$$;

CREATE PROCEDURE public.product_catalog_refresh_complete_with_run(IN p_refresh_id bigint, IN p_run_id bigint, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_deactivated_count integer, IN p_reactivated_count integer, IN p_finished_at timestamp with time zone, IN p_run_note text DEFAULT NULL::text)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog_refresh
set finished_at       = p_finished_at,
    status            = 'ok',
    discovered_count  = greatest(coalesce(p_discovered_count, 0), 0),
    accepted_count    = greatest(coalesce(p_accepted_count, 0), 0),
    inserted_count    = greatest(coalesce(p_inserted_count, 0), 0),
    updated_count     = greatest(coalesce(p_updated_count, 0), 0),
    deactivated_count = greatest(coalesce(p_deactivated_count, 0), 0),
    reactivated_count = greatest(coalesce(p_reactivated_count, 0), 0),
    error_code        = null,
    error_message     = null,
    updated_at        = now()
where id = p_refresh_id
  and status = 'running';

if
not found then
    raise exception 'Running product_catalog_refresh % was not found for completion.', p_refresh_id;
end if;

update crawler_run
set status      = routine_support_run_status('ok'),
    note        = routine_support_trim_nullable(p_run_note, 255),
    finished_at = greatest(p_finished_at, started_at)
where id = p_run_id;

if
not found then
    raise exception 'crawler_run % was not found for catalog refresh completion.', p_run_id;
end if;
end;
$$;

CREATE PROCEDURE public.product_catalog_refresh_fail(IN p_refresh_id bigint, IN p_status text, IN p_error_code text, IN p_error_message text, IN p_finished_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog_refresh
set finished_at   = p_finished_at,
    status        = case
                        when routine_support_trim_required(p_status, 20) = 'cancelled' then 'cancelled'
                        else 'error'
        end,
    error_code    = routine_support_trim_required(p_error_code, 100),
    error_message = routine_support_trim_nullable(p_error_message, 1000),
    updated_at    = now()
where id = p_refresh_id
  and status = 'running';
end;
$$;

CREATE PROCEDURE public.product_catalog_refresh_fail_with_run(IN p_refresh_id bigint, IN p_run_id bigint, IN p_status text, IN p_error_code text, IN p_error_message text, IN p_finished_at timestamp with time zone, IN p_run_status text, IN p_run_note text DEFAULT NULL::text)
    LANGUAGE plpgsql
    AS $$
begin
update product_catalog_refresh
set finished_at   = p_finished_at,
    status        = case
                        when routine_support_trim_required(p_status, 20) = 'cancelled' then 'cancelled'
                        else 'error'
        end,
    error_code    = routine_support_trim_required(p_error_code, 100),
    error_message = routine_support_trim_nullable(p_error_message, 1000),
    updated_at    = now()
where id = p_refresh_id
  and status = 'running';

if
not found then
    raise exception 'Running product_catalog_refresh % was not found for failure.', p_refresh_id;
end if;

update crawler_run
set status      = routine_support_run_status(p_run_status),
    note        = routine_support_trim_nullable(p_run_note, 255),
    finished_at = greatest(p_finished_at, started_at)
where id = p_run_id;

if
not found then
    raise exception 'crawler_run % was not found for catalog refresh failure.', p_run_id;
end if;
end;
$$;

CREATE FUNCTION public.product_catalog_refresh_get_by_id(p_refresh_id bigint) RETURNS TABLE(id bigint, source character varying, discovery_source character varying, started_at timestamp with time zone, finished_at timestamp with time zone, status character varying, discovered_count integer, accepted_count integer, inserted_count integer, updated_count integer, deactivated_count integer, reactivated_count integer, error_code character varying, error_message character varying)
    LANGUAGE sql
    AS $$
select refresh.id,
       refresh.source,
       refresh.discovery_source,
       refresh.started_at,
       refresh.finished_at,
       refresh.status,
       refresh.discovered_count,
       refresh.accepted_count,
       refresh.inserted_count,
       refresh.updated_count,
       refresh.deactivated_count,
       refresh.reactivated_count,
       refresh.error_code,
       refresh.error_message
from product_catalog_refresh refresh
where refresh.id = p_refresh_id;
$$;

CREATE FUNCTION public.product_catalog_refresh_start(p_source text, p_discovery_source text, p_started_at timestamp with time zone, p_abandoned_before timestamp with time zone) RETURNS bigint
    LANGUAGE plpgsql
    AS $$
declare
v_id bigint;
begin
update product_catalog_refresh
set status        = 'error',
    finished_at   = coalesce(p_started_at, now()),
    error_code    = 'catalog_refresh_abandoned',
    error_message = 'running refresh session exceeded configured timeout',
    updated_at    = now()
where source = routine_support_trim_required(p_source, 50)
  and status = 'running'
  and started_at < p_abandoned_before;

if
exists (
    select 1
    from product_catalog_refresh
    where source = routine_support_trim_required(p_source, 50)
      and status = 'running'
) then
    return 0;
end if;

insert into product_catalog_refresh(source,
                                    discovery_source,
                                    started_at,
                                    status,
                                    created_at,
                                    updated_at)
values (routine_support_trim_required(p_source, 50),
        routine_support_trim_required(p_discovery_source, 50),
        coalesce(p_started_at, now()),
        'running',
        now(),
        now()) returning id
into v_id;

return v_id;
end;
$$;

CREATE FUNCTION public.product_catalog_release_reservations(p_catalog_item_ids bigint[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
declare
v_count integer;
begin
with released as (
update product_catalog catalog
set reserved_at = null, reserved_until = null, reserved_by = null, updated_at = now()
where catalog.id = any (coalesce (p_catalog_item_ids
    , array[]::bigint[]))
  and (catalog.reserved_at is not null
   or catalog.reserved_until is not null
   or catalog.reserved_by is not null)
    returning 1
    )
select count(*)
into v_count
from released;

return coalesce(v_count, 0);
end;
$$;

CREATE FUNCTION public.product_catalog_upsert_discovered(p_refresh_id bigint, p_items text) RETURNS TABLE(received_count integer, inserted_count integer, updated_count integer, reactivated_count integer)
    LANGUAGE plpgsql
    AS $$
begin
return query with incoming as (
        select
            routine_support_trim_required(item.source, 50)::varchar(50) as source,
            routine_support_trim_required(item.url, 1024)::varchar(1024) as url,
            routine_support_trim_required(item.normalized_url, 1024)::varchar(1024) as normalized_url,
            routine_support_trim_nullable(item.external_id, 200)::varchar(200) as external_id,
            routine_support_trim_nullable(item.slug, 300)::varchar(300) as slug,
            coalesce(item.discovered_at_utc, now()) as discovered_at
        from jsonb_to_recordset(coalesce(nullif(p_items, ''), '[]')::jsonb) as item(
            source text,
            url text,
            normalized_url text,
            external_id text,
            slug text,
            discovered_at_utc timestamptz)
    ),
    valid as (
        select *
        from incoming
        where source <> ''
          and url <> ''
          and normalized_url <> ''
    ),
    existing as (
        select catalog.source, catalog.normalized_url, catalog.is_active
        from product_catalog catalog
        join valid
          on catalog.source = valid.source
         and catalog.normalized_url = valid.normalized_url
    ),
    upserted as (
        insert into product_catalog(
            source,
            url,
            normalized_url,
            external_id,
            slug,
            first_discovered_at,
            last_discovered_at,
            is_active,
            consecutive_errors,
            last_seen_refresh_id,
            created_at,
            updated_at)
        select
            source,
            url,
            normalized_url,
            external_id,
            slug,
            discovered_at,
            discovered_at,
            true,
            0,
            nullif(p_refresh_id, 0),
            now(),
            now()
        from valid
        on conflict (source, normalized_url)
        do update
        set url = excluded.url,
            external_id = coalesce(nullif(excluded.external_id, ''), product_catalog.external_id),
            slug = coalesce(nullif(excluded.slug, ''), product_catalog.slug),
            last_discovered_at = excluded.last_discovered_at,
            last_seen_refresh_id = nullif(p_refresh_id, 0),
            deactivated_at = null,
            reactivated_at = case
                when product_catalog.is_active = false then excluded.last_discovered_at
                else product_catalog.reactivated_at
            end,
            next_check_at = case
                when product_catalog.is_active = false then null
                else product_catalog.next_check_at
            end,
            is_active = true,
            updated_at = now()
        returning product_catalog.source, product_catalog.normalized_url
    )
select (select count(*) from valid)::integer as received_count, (select count(*)
                                                                 from upserted
                                                                 where not exists (select 1
                                                                                   from existing
                                                                                   where existing.source = upserted.source
                                                                                     and existing.normalized_url = upserted.normalized_url))::integer as inserted_count, (select count(*)
                                                                                                                                                                          from upserted
                                                                                                                                                                          where exists (select 1
                                                                                                                                                                                        from existing
                                                                                                                                                                                        where existing.source = upserted.source
                                                                                                                                                                                          and existing.normalized_url = upserted.normalized_url
                                                                                                                                                                                          and existing.is_active = true)) ::integer as updated_count, (select count(*)
                                                                                                                                                                                                                                                       from upserted
                                                                                                                                                                                                                                                       where exists (select 1
                                                                                                                                                                                                                                                                     from existing
                                                                                                                                                                                                                                                                     where existing.source = upserted.source
                                                                                                                                                                                                                                                                       and existing.normalized_url = upserted.normalized_url
                                                                                                                                                                                                                                                                       and existing.is_active = false)) ::integer as reactivated_count;
end;
$$;

CREATE FUNCTION public.routine_support_queue_status(p_status text) RETURNS character varying
    LANGUAGE sql
    AS $$
select case lower(coalesce(btrim(p_status), ''))
           when 'reserved' then 'reserved'
           when 'retry' then 'retry'
           when 'succeeded' then 'succeeded'
           when 'dead' then 'dead'
           else 'pending'
           end::varchar(32);
$$;

CREATE FUNCTION public.routine_support_run_status(p_status text) RETURNS character varying
    LANGUAGE sql
    AS $$
select case lower(coalesce(btrim(p_status), ''))
           when 'running' then 'running'
           when 'ok' then 'ok'
           else 'error'
           end::varchar(32);
$$;

CREATE FUNCTION public.routine_support_trim_nullable(p_value text, p_max_length integer) RETURNS text
    LANGUAGE sql
    AS $$
select case
           when p_value is null or btrim(p_value) = '' then null
           else left (btrim(p_value), greatest(p_max_length, 0))
end;
$$;

CREATE FUNCTION public.routine_support_trim_required(p_value text, p_max_length integer) RETURNS text
    LANGUAGE sql
    AS $$
select coalesce(routine_support_trim_nullable(p_value, p_max_length), '');
$$;

SET default_tablespace = '';

SET default_table_access_method = heap;

CREATE TABLE public.crawl_error (
    id bigint NOT NULL,
    run_id bigint NOT NULL,
    queue_id bigint,
    product_id bigint,
    url character varying(1024),
    error_code character varying(64),
    http_status integer,
    error_message character varying(512),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.crawl_error ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.crawl_error_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.crawler_run (
    id bigint NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    finished_at timestamp with time zone,
    status character varying(32) NOT NULL,
    source character varying(64) NOT NULL,
    note character varying(255),
    run_type character varying(50) DEFAULT 'legacy'::character varying NOT NULL,
    discovery_source character varying(50),
    duration_ms bigint,
    discovered_count integer DEFAULT 0 NOT NULL,
    accepted_count integer DEFAULT 0 NOT NULL,
    inserted_count integer DEFAULT 0 NOT NULL,
    updated_count integer DEFAULT 0 NOT NULL,
    reactivated_count integer DEFAULT 0 NOT NULL,
    deactivated_count integer DEFAULT 0 NOT NULL,
    selected_count integer DEFAULT 0 NOT NULL,
    enqueued_count integer DEFAULT 0 NOT NULL,
    succeeded_count integer DEFAULT 0 NOT NULL,
    retry_count integer DEFAULT 0 NOT NULL,
    dead_count integer DEFAULT 0 NOT NULL,
    failed_count integer DEFAULT 0 NOT NULL,
    products_created_count integer DEFAULT 0 NOT NULL,
    products_updated_count integer DEFAULT 0 NOT NULL,
    snapshots_created_count integer DEFAULT 0 NOT NULL,
    errors_created_count integer DEFAULT 0 NOT NULL,
    error_code character varying(100),
    error_message character varying(1000),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_crawler_run_dates CHECK (((finished_at IS NULL) OR (finished_at >= started_at))),
    CONSTRAINT ck_crawler_run_non_negative CHECK ((((duration_ms IS NULL) OR (duration_ms >= 0)) AND (discovered_count >= 0) AND (accepted_count >= 0) AND (inserted_count >= 0) AND (updated_count >= 0) AND (reactivated_count >= 0) AND (deactivated_count >= 0) AND (selected_count >= 0) AND (enqueued_count >= 0) AND (succeeded_count >= 0) AND (retry_count >= 0) AND (dead_count >= 0) AND (failed_count >= 0) AND (products_created_count >= 0) AND (products_updated_count >= 0) AND (snapshots_created_count >= 0) AND (errors_created_count >= 0))),
    CONSTRAINT ck_crawler_run_run_type CHECK (((run_type)::text = ANY ((ARRAY['catalog-refresh'::character varying, 'price-collection'::character varying, 'legacy'::character varying])::text[])))
);

ALTER TABLE public.crawler_run ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.crawler_run_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.crawler_run_stage (
    id bigint NOT NULL,
    run_id bigint NOT NULL,
    stage character varying(100) NOT NULL,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone NOT NULL,
    duration_ms bigint NOT NULL,
    item_count integer,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_crawler_run_stage_dates CHECK ((finished_at >= started_at)),
    CONSTRAINT crawler_run_stage_duration_ms_check CHECK ((duration_ms >= 0)),
    CONSTRAINT crawler_run_stage_item_count_check CHECK (((item_count IS NULL) OR (item_count >= 0)))
);

ALTER TABLE public.crawler_run_stage ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.crawler_run_stage_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.db_routine_script (
    script_name character varying(255) NOT NULL,
    script_hash character varying(64) NOT NULL,
    applied_at timestamp with time zone DEFAULT now() NOT NULL
);

CREATE TABLE public.ingestion_run (
    ingestion_run_id bigint NOT NULL,
    crawler_run_id bigint NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    finished_at timestamp with time zone,
    status character varying(32) NOT NULL,
    error_code character varying(128),
    error_message character varying(512)
);

CREATE SEQUENCE public.ingestion_run_ingestion_run_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

ALTER SEQUENCE public.ingestion_run_ingestion_run_id_seq OWNED BY public.ingestion_run.ingestion_run_id;

CREATE TABLE public.price_collect_queue (
    id bigint NOT NULL,
    run_id bigint NOT NULL,
    product_catalog_id bigint,
    url character varying(1024) NOT NULL,
    page_kind character varying(32) DEFAULT 'product_page'::character varying NOT NULL,
    status character varying(32) NOT NULL,
    attempt integer DEFAULT 0 NOT NULL,
    max_attempts integer DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone,
    reserved_at timestamp with time zone,
    lease_until timestamp with time zone,
    reserved_by character varying(128),
    idempotency_key character varying(128),
    last_error_code character varying(64),
    last_http_status integer,
    last_error_message character varying(512),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    finished_at timestamp with time zone
);

ALTER TABLE public.price_collect_queue ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.price_collect_queue_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.price_snapshot (
    id bigint NOT NULL,
    run_id bigint NOT NULL,
    product_id bigint NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    price numeric(18,2),
    old_price numeric(18,2),
    promo_flag boolean DEFAULT false NOT NULL,
    in_stock boolean DEFAULT false NOT NULL,
    queue_id bigint
);

ALTER TABLE public.price_snapshot ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.price_snapshot_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.product (
    id bigint NOT NULL,
    external_id character varying(64),
    name character varying(512) NOT NULL,
    url character varying(1024) NOT NULL,
    slug character varying(512),
    pack_value numeric(18,6),
    pack_unit character varying(16),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.product_catalog (
    id bigint NOT NULL,
    source character varying(50) NOT NULL,
    url character varying(1024) NOT NULL,
    normalized_url character varying(1024) NOT NULL,
    external_id character varying(200),
    slug character varying(300),
    first_discovered_at timestamp with time zone NOT NULL,
    last_discovered_at timestamp with time zone NOT NULL,
    last_checked_at timestamp with time zone,
    next_check_at timestamp with time zone,
    reserved_at timestamp with time zone,
    reserved_until timestamp with time zone,
    reserved_by character varying(200),
    is_active boolean DEFAULT true NOT NULL,
    consecutive_errors integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    last_seen_refresh_id bigint,
    deactivated_at timestamp with time zone,
    reactivated_at timestamp with time zone,
    CONSTRAINT ck_product_catalog_consecutive_errors_non_negative CHECK ((consecutive_errors >= 0))
);

ALTER TABLE public.product_catalog ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.product_catalog_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.product_catalog_refresh (
    id bigint NOT NULL,
    source character varying(50) NOT NULL,
    discovery_source character varying(50) NOT NULL,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone,
    status character varying(20) NOT NULL,
    discovered_count integer DEFAULT 0 NOT NULL,
    accepted_count integer DEFAULT 0 NOT NULL,
    inserted_count integer DEFAULT 0 NOT NULL,
    updated_count integer DEFAULT 0 NOT NULL,
    deactivated_count integer DEFAULT 0 NOT NULL,
    reactivated_count integer DEFAULT 0 NOT NULL,
    error_code character varying(100),
    error_message character varying(1000),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_product_catalog_refresh_counts_non_negative CHECK (((discovered_count >= 0) AND (accepted_count >= 0) AND (inserted_count >= 0) AND (updated_count >= 0) AND (deactivated_count >= 0) AND (reactivated_count >= 0))),
    CONSTRAINT ck_product_catalog_refresh_status CHECK (((status)::text = ANY ((ARRAY['running'::character varying, 'ok'::character varying, 'error'::character varying, 'cancelled'::character varying])::text[])))
);

ALTER TABLE public.product_catalog_refresh ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.product_catalog_refresh_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

ALTER TABLE public.product ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.product_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

ALTER TABLE ONLY public.ingestion_run ALTER COLUMN ingestion_run_id SET DEFAULT nextval('public.ingestion_run_ingestion_run_id_seq'::regclass);

ALTER TABLE ONLY public.crawl_error
    ADD CONSTRAINT crawl_error_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.crawler_run
    ADD CONSTRAINT crawler_run_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.crawler_run_stage
    ADD CONSTRAINT crawler_run_stage_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.db_routine_script
    ADD CONSTRAINT db_routine_script_pkey PRIMARY KEY (script_name);

ALTER TABLE ONLY public.ingestion_run
    ADD CONSTRAINT ingestion_run_pkey PRIMARY KEY (ingestion_run_id);

ALTER TABLE ONLY public.price_collect_queue
    ADD CONSTRAINT price_collect_queue_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.price_snapshot
    ADD CONSTRAINT price_snapshot_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.product_catalog
    ADD CONSTRAINT product_catalog_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.product_catalog_refresh
    ADD CONSTRAINT product_catalog_refresh_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.product
    ADD CONSTRAINT product_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.crawler_run_stage
    ADD CONSTRAINT uq_crawler_run_stage UNIQUE (run_id, stage);

CREATE INDEX ix_crawl_error_product_id ON public.crawl_error USING btree (product_id);

CREATE INDEX ix_crawl_error_run_id ON public.crawl_error USING btree (run_id);

CREATE INDEX ix_crawler_run_run_type_started_at ON public.crawler_run USING btree (run_type, started_at DESC);

CREATE INDEX ix_crawler_run_source_started_at_desc ON public.crawler_run USING btree (source, started_at DESC);

CREATE INDEX ix_crawler_run_stage_run_id ON public.crawler_run_stage USING btree (run_id);

CREATE INDEX ix_crawler_run_started_at ON public.crawler_run USING btree (started_at DESC);

CREATE INDEX ix_crawler_run_status_started_at ON public.crawler_run USING btree (status, started_at DESC);

CREATE INDEX ix_price_collect_queue_lease ON public.price_collect_queue USING btree (status, lease_until);

CREATE INDEX ix_price_collect_queue_pick ON public.price_collect_queue USING btree (status, next_attempt_at, id);

CREATE INDEX ix_price_collect_queue_product_catalog_id ON public.price_collect_queue USING btree (product_catalog_id);

CREATE INDEX ix_price_snapshot_product_captured_at_desc ON public.price_snapshot USING btree (product_id, captured_at DESC);

CREATE INDEX ix_price_snapshot_run_id ON public.price_snapshot USING btree (run_id);

CREATE INDEX ix_product_catalog_due ON public.product_catalog USING btree (is_active, next_check_at, reserved_until, last_checked_at, id);

CREATE INDEX ix_product_catalog_last_discovered_at ON public.product_catalog USING btree (last_discovered_at);

CREATE INDEX ix_product_catalog_last_seen_refresh_id ON public.product_catalog USING btree (last_seen_refresh_id);

CREATE INDEX ix_product_catalog_reservation ON public.product_catalog USING btree (reserved_until, id) WHERE (reserved_until IS NOT NULL);

CREATE INDEX ix_product_external_id ON public.product USING btree (external_id);

CREATE UNIQUE INDEX ux_price_collect_queue_idempotency ON public.price_collect_queue USING btree (idempotency_key) WHERE (idempotency_key IS NOT NULL);

CREATE UNIQUE INDEX ux_price_collect_queue_run_url ON public.price_collect_queue USING btree (run_id, url);

CREATE UNIQUE INDEX ux_product_catalog_refresh_running_source ON public.product_catalog_refresh USING btree (source) WHERE ((status)::text = 'running'::text);

CREATE UNIQUE INDEX ux_product_catalog_source_normalized_url ON public.product_catalog USING btree (source, normalized_url);

CREATE UNIQUE INDEX ux_product_url ON public.product USING btree (url);

ALTER TABLE ONLY public.crawl_error
    ADD CONSTRAINT crawl_error_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.product(id);

ALTER TABLE ONLY public.crawl_error
    ADD CONSTRAINT crawl_error_queue_id_fkey FOREIGN KEY (queue_id) REFERENCES public.price_collect_queue(id);

ALTER TABLE ONLY public.crawl_error
    ADD CONSTRAINT crawl_error_run_id_fkey FOREIGN KEY (run_id) REFERENCES public.crawler_run(id);

ALTER TABLE ONLY public.crawler_run_stage
    ADD CONSTRAINT crawler_run_stage_run_id_fkey FOREIGN KEY (run_id) REFERENCES public.crawler_run(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.price_collect_queue
    ADD CONSTRAINT fk_price_collect_queue_product_catalog FOREIGN KEY (product_catalog_id) REFERENCES public.product_catalog(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.product_catalog
    ADD CONSTRAINT fk_product_catalog_last_seen_refresh FOREIGN KEY (last_seen_refresh_id) REFERENCES public.product_catalog_refresh(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.ingestion_run
    ADD CONSTRAINT ingestion_run_crawler_run_id_fkey FOREIGN KEY (crawler_run_id) REFERENCES public.crawler_run(id);

ALTER TABLE ONLY public.price_collect_queue
    ADD CONSTRAINT price_collect_queue_run_id_fkey FOREIGN KEY (run_id) REFERENCES public.crawler_run(id);

ALTER TABLE ONLY public.price_snapshot
    ADD CONSTRAINT price_snapshot_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.product(id);

ALTER TABLE ONLY public.price_snapshot
    ADD CONSTRAINT price_snapshot_queue_id_fkey FOREIGN KEY (queue_id) REFERENCES public.price_collect_queue(id);

ALTER TABLE ONLY public.price_snapshot
    ADD CONSTRAINT price_snapshot_run_id_fkey FOREIGN KEY (run_id) REFERENCES public.crawler_run(id);

CREATE TABLE public.schema_version
(
    version integer NOT NULL PRIMARY KEY,
    migration_name character varying(200) NOT NULL,
    applied_at_utc timestamp with time zone NOT NULL DEFAULT now(),
    application_version character varying(50),
    checksum character varying(128)
);

INSERT INTO public.db_routine_script(script_name, script_hash, applied_at)
VALUES
    ('001__routine_support_text.sql', '8eb7461e125e5c948bcea9f41428af79fc8df3325233a0e66cfb95195a52a9ae', now()),
    ('010__run_error_routines.sql', '662dc24862ecd0cc5e5d28361fb9521dca2310d25d7d76fd2b1670ec81f01cd0', now()),
    ('020__queue_routines.sql', '95e4a28002237526b90866d7910f5a9b5ac9a5ae3b49d23ae36807fd28f3fbbd', now()),
    ('030__observation_routines.sql', 'e1e536a69ad829f932df2ab93e2612b8077677add9f6702aa1fd8cd1b4511c69', now()),
    ('040__product_catalog_routines.sql', '365f7729f69d1f58494f1f1590edb69d3547467fd0af11a0f06b32858983e365', now()),
    ('050__crawler_run_statistics.sql', '81cd1ee85e56336aceba9c3f4f3c7bf69be300cf8341740a52c1b65d3453cc0c', now());

INSERT INTO public.schema_version(version, migration_name, application_version, checksum)
VALUES (1, '0001_baseline', 'v0.4.1-alpha', NULL);

COMMIT;
