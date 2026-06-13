create
or replace function product_catalog_refresh_start(
    p_source text,
    p_discovery_source text,
    p_started_at timestamptz)
returns bigint
language plpgsql
as $$
declare
v_id bigint;
begin
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

create
or replace procedure product_catalog_refresh_complete(
    p_refresh_id bigint,
    p_discovered_count integer,
    p_accepted_count integer,
    p_inserted_count integer,
    p_updated_count integer,
    p_deactivated_count integer,
    p_reactivated_count integer,
    p_finished_at timestamptz)
language plpgsql
as $$
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

create
or replace procedure product_catalog_refresh_fail(
    p_refresh_id bigint,
    p_status text,
    p_error_code text,
    p_error_message text,
    p_finished_at timestamptz)
language plpgsql
as $$
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

create
or replace function product_catalog_refresh_get_by_id(
    p_refresh_id bigint)
returns table(
    id bigint,
    source varchar(50),
    discovery_source varchar(50),
    started_at timestamptz,
    finished_at timestamptz,
    status varchar(20),
    discovered_count integer,
    accepted_count integer,
    inserted_count integer,
    updated_count integer,
    deactivated_count integer,
    reactivated_count integer,
    error_code varchar(100),
    error_message varchar(1000))
language sql
as $$
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

create
or replace function product_catalog_upsert_discovered(
    p_refresh_id bigint,
    p_items text)
returns table(
    received_count integer,
    inserted_count integer,
    updated_count integer,
    reactivated_count integer)
language plpgsql
as $$
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

create
or replace function product_catalog_get_active_count(
    p_source text)
returns integer
language sql
as $$
select count(*) ::integer
from product_catalog catalog
where catalog.source = routine_support_trim_required(p_source
    , 50)
  and catalog.is_active = true;
$$;

create
or replace function product_catalog_deactivate_missing(
    p_source text,
    p_current_refresh_id bigint,
    p_not_seen_since timestamptz,
    p_deactivated_at timestamptz)
returns integer
language plpgsql
as $$
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

create
or replace function product_catalog_get_by_id(
    p_id bigint)
returns table(
    id bigint,
    source varchar(50),
    url varchar(1024),
    normalized_url varchar(1024),
    external_id varchar(200),
    slug varchar(300),
    first_discovered_at timestamptz,
    last_discovered_at timestamptz,
    last_checked_at timestamptz,
    next_check_at timestamptz,
    is_active boolean,
    consecutive_errors integer)
language sql
as $$
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

create
or replace function product_catalog_get_by_source_normalized_url(
    p_source text,
    p_normalized_url text)
returns table(
    id bigint,
    source varchar(50),
    url varchar(1024),
    normalized_url varchar(1024),
    external_id varchar(200),
    slug varchar(300),
    first_discovered_at timestamptz,
    last_discovered_at timestamptz,
    last_checked_at timestamptz,
    next_check_at timestamptz,
    is_active boolean,
    consecutive_errors integer)
language sql
as $$
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

create
or replace function product_catalog_get_due(
    p_limit integer,
    p_now timestamptz,
    p_lease_seconds integer,
    p_worker_id text)
returns table(
    id bigint,
    source varchar(50),
    url varchar(1024),
    normalized_url varchar(1024),
    external_id varchar(200),
    slug varchar(300),
    first_discovered_at timestamptz,
    last_discovered_at timestamptz,
    last_checked_at timestamptz,
    next_check_at timestamptz,
    is_active boolean,
    consecutive_errors integer)
language sql
as $$
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

create
or replace procedure product_catalog_mark_checked(
    p_catalog_item_id bigint,
    p_checked_at timestamptz,
    p_next_check_at timestamptz,
    p_external_id text,
    p_slug text)
language plpgsql
as $$
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

create
or replace procedure product_catalog_mark_failed(
    p_catalog_item_id bigint,
    p_attempted_at timestamptz,
    p_next_check_at timestamptz)
language plpgsql
as $$
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

create
or replace function product_catalog_release_reservations(
    p_catalog_item_ids bigint[])
returns integer
language plpgsql
as $$
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
