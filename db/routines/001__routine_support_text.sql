create
or replace function routine_support_trim_nullable(
    p_value text,
    p_max_length integer)
returns text
language sql
as $$
select case
           when p_value is null or btrim(p_value) = '' then null
           else left (btrim(p_value), greatest(p_max_length, 0))
end;
$$;

create
or replace function routine_support_trim_required(
    p_value text,
    p_max_length integer)
returns text
language sql
as $$
select coalesce(routine_support_trim_nullable(p_value, p_max_length), '');
$$;
