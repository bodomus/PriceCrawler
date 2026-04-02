create
or replace function routine_support_queue_status(
    p_status text)
returns varchar(32)
language sql
as $$
select case lower(coalesce(btrim(p_status), ''))
           when 'reserved' then 'reserved'
           when 'retry' then 'retry'
           when 'succeeded' then 'succeeded'
           when 'dead' then 'dead'
           else 'pending'
           end::varchar(32);
$$;

create
or replace function price_collect_queue_enqueue(
    p_run_id bigint,
    p_urls text[],
    p_idempotency_keys text[],
    p_max_attempts integer)
returns integer
language plpgsql
as $$
declare
v_count integer;
begin
with inserted as (
insert
into price_collect_queue(run_id,
                         url,
                         status,
                         attempt,
                         max_attempts,
                         next_attempt_at,
                         idempotency_key,
                         created_at,
                         updated_at)
select p_run_id,
       routine_support_trim_required(x.url, 1024),
       routine_support_queue_status('pending'),
       0,
       greatest(coalesce(p_max_attempts, 0), 1),
       now(),
       routine_support_trim_required(x.idempotency_key, 128),
       now(),
       now()
from unnest(p_urls, p_idempotency_keys) as x(url, idempotency_key) on conflict (run_id, url) do nothing
        returning 1
    )
select count(*)
into v_count
from inserted;

return coalesce(v_count, 0);
end;
$$;

create
or replace function price_collect_queue_reserve_batch(
    p_run_id bigint,
    p_batch_size integer,
    p_worker_id text,
    p_lease_seconds integer)
returns table(
    id bigint,
    url varchar(1024),
    attempt integer,
    max_attempts integer,
    idempotency_key varchar(128))
language sql
as $$
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
        returning queue.id, queue.url, queue.attempt, queue.max_attempts, queue.idempotency_key
    )
select updated.id,
       updated.url,
       updated.attempt,
       updated.max_attempts,
       coalesce(updated.idempotency_key, '')
from updated
order by updated.id;
$$;

create
or replace procedure price_collect_queue_mark_succeeded(
    p_queue_id bigint)
language plpgsql
as $$
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

create
or replace procedure price_collect_queue_mark_retry(
    p_queue_id bigint,
    p_error_code text,
    p_http_status integer,
    p_error_message text,
    p_next_attempt_at timestamptz)
language plpgsql
as $$
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

create
or replace procedure price_collect_queue_mark_dead(
    p_queue_id bigint,
    p_error_code text,
    p_http_status integer,
    p_error_message text)
language plpgsql
as $$
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

create
or replace function price_collect_queue_reap_expired(
    p_run_id bigint)
returns integer
language plpgsql
as $$
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

create
or replace function price_collect_queue_has_outstanding(
    p_run_id bigint)
returns boolean
language sql
as $$
select exists (select 1
               from price_collect_queue
               where run_id = p_run_id
                 and status in (
                                routine_support_queue_status('pending'),
                                routine_support_queue_status('retry'),
                                routine_support_queue_status('reserved')));
$$;

create
or replace function price_collect_queue_get_run_stats(
    p_run_id bigint)
returns table(
    pending_count integer,
    reserved_count integer,
    retry_count integer,
    succeeded_count integer,
    dead_count integer)
language sql
as $$
select count(*) filter (where status = routine_support_queue_status('pending'))::integer as pending_count, count(*) filter (where status = routine_support_queue_status('reserved'))::integer as reserved_count, count(*) filter (where status = routine_support_queue_status('retry'))::integer as retry_count, count(*) filter (where status = routine_support_queue_status('succeeded'))::integer as succeeded_count, count(*) filter (where status = routine_support_queue_status('dead'))::integer as dead_count
from price_collect_queue
where run_id = p_run_id;
$$;
