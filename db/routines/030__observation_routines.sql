create
or replace function price_observation_store(
    p_run_id bigint,
    p_queue_id bigint,
    p_external_id text,
    p_name text,
    p_url text,
    p_slug text,
    p_pack_value numeric(18, 6),
    p_pack_unit text,
    p_price numeric(18, 2),
    p_old_price numeric(18, 2),
    p_promo_flag boolean,
    p_in_stock boolean,
    p_observed_at timestamptz)
returns table(
    product_id bigint,
    snapshot_id bigint,
    snapshot_created boolean)
language plpgsql
as $$
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

select product_row.id
into v_existing_product_id
from product as product_row
where product_row.url = v_url
   or (v_external_id is not null and product_row.external_id = v_external_id)
order by case when product_row.url = v_url then 0 else 1 end, product_row.id limit 1
    for
update;

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
    return
next;
end;
$$;
