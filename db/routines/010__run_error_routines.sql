create
or replace function routine_support_run_status(
    p_status text)
returns varchar(32)
language sql
as $$
select case lower(coalesce(btrim(p_status), ''))
           when 'running' then 'running'
           when 'ok' then 'ok'
           else 'error'
           end::varchar(32);
$$;

create
or replace function crawler_run_start(
    p_source text)
returns bigint
language plpgsql
as $$
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

create
or replace procedure crawler_run_finish(
    p_run_id bigint,
    p_status text,
    p_note text default null)
language plpgsql
as $$
begin
update crawler_run
set status      = routine_support_run_status(p_status),
    note        = routine_support_trim_nullable(p_note, 255),
    finished_at = now()
where id = p_run_id;
end;
$$;

create
or replace function ingestion_run_start(
    p_crawler_run_id bigint)
returns bigint
language plpgsql
as $$
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

create
or replace procedure ingestion_run_finish(
    p_ingestion_run_id bigint,
    p_status text,
    p_error_code text default null,
    p_error_message text default null)
language plpgsql
as $$
begin
update ingestion_run
set status        = routine_support_run_status(p_status),
    error_code    = routine_support_trim_nullable(p_error_code, 128),
    error_message = routine_support_trim_nullable(p_error_message, 512),
    finished_at   = now()
where ingestion_run_id = p_ingestion_run_id;
end;
$$;

create
or replace function crawl_error_add(
    p_run_id bigint,
    p_queue_id bigint,
    p_product_id bigint,
    p_url text,
    p_created_at timestamptz,
    p_error_code text,
    p_http_status integer,
    p_error_message text)
returns bigint
language plpgsql
as $$
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
