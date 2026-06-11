create
or replace function product_catalog_upsert_discovered(
    p_items text)
returns table(
    received_count integer,
    inserted_count integer,
    updated_count integer)
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
        select catalog.source, catalog.normalized_url
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
            now(),
            now()
        from valid
        on conflict (source, normalized_url)
        do update
        set url = excluded.url,
            external_id = coalesce(nullif(excluded.external_id, ''), product_catalog.external_id),
            slug = coalesce(nullif(excluded.slug, ''), product_catalog.slug),
            last_discovered_at = excluded.last_discovered_at,
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
                                                                                                                                                                                          and existing.normalized_url = upserted.normalized_url)) ::integer as updated_count;
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

