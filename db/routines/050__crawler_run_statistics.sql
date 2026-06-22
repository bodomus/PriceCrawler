create
or replace function crawler_run_start(
    p_run_type text,
    p_source text,
    p_discovery_source text)
returns bigint language plpgsql as $$
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

create
or replace procedure crawler_run_complete(
    p_run_id bigint, p_status text,
    p_discovered_count integer, p_accepted_count integer, p_inserted_count integer,
    p_updated_count integer, p_reactivated_count integer, p_deactivated_count integer,
    p_selected_count integer, p_enqueued_count integer, p_succeeded_count integer,
    p_retry_count integer, p_dead_count integer, p_failed_count integer,
    p_products_created_count integer, p_products_updated_count integer,
    p_snapshots_created_count integer, p_errors_created_count integer,
    p_stages_json text, p_note text default null, p_error_code text default null,
    p_error_message text default null)
language plpgsql as $$
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
       v_finished_at - make_interval(secs = > timing.effective_duration_ms::double precision / 1000.0),
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

drop function if exists crawler_run_get_by_id(bigint);

create function crawler_run_get_by_id(p_run_id bigint)
    returns table
            (
                id                      bigint,
                run_type                varchar(50),
                source                  varchar(64),
                discovery_source        varchar(50),
                status                  varchar(32),
                started_at              timestamptz,
                finished_at             timestamptz,
                duration_ms             bigint,
                discovered_count        integer,
                accepted_count          integer,
                inserted_count          integer,
                updated_count           integer,
                reactivated_count       integer,
                deactivated_count       integer,
                selected_count          integer,
                enqueued_count          integer,
                succeeded_count         integer,
                retry_count             integer,
                dead_count              integer,
                failed_count            integer,
                products_created_count  integer,
                products_updated_count  integer,
                snapshots_created_count integer,
                errors_created_count    integer,
                error_code              varchar(100),
                error_message           varchar(1000),
                note                    varchar(255)
            )
    language sql stable as $$
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

create
or replace function crawler_run_stage_get(p_run_id bigint)
returns table(stage varchar(100), duration_ms bigint, item_count integer)
language sql stable as $$
select s.stage, s.duration_ms, s.item_count
from crawler_run_stage s
where s.run_id = p_run_id
order by s.id;
$$;

create
or replace function crawler_run_get_recent(p_limit integer, p_run_type text, p_status text)
returns table(id bigint, run_type varchar(50), source varchar(64), status varchar(32), started_at timestamptz,
              finished_at timestamptz, duration_ms bigint, primary_count integer, succeeded_count integer,
              failed_count integer, error_code varchar(100))
language sql stable as $$
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

create
or replace function crawler_run_get_aggregate(p_from timestamptz, p_to timestamptz, p_run_type text)
returns table(total_runs integer, successful_runs integer, failed_runs integer, total_duration_ms bigint,
              average_duration_ms double precision, total_discovered bigint, total_accepted bigint,
              total_selected bigint, total_succeeded bigint, total_dead bigint,
              total_snapshots_created bigint, total_errors_created bigint)
language sql stable as $$
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
