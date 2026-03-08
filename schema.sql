create table if not exists Crawler_run (
    run_id        bigserial primary key,
    started_at    timestamptz not null default now(),
    finished_at   timestamptz null,
    status        varchar(32) not null,
    source        varchar(64) not null,
    note          varchar(255) null
);

create table if not exists ingestion_run (
    ingestion_run_id bigserial primary key,
    crawler_run_id   bigint not null references crawler_run(run_id),
    started_at       timestamptz not null default now(),
    finished_at      timestamptz null,
    status           varchar(32) not null,
    error_code       varchar(128) null,
    error_message    varchar(512) null
);

create table if not exists price_collect_queue (
    queue_id           bigserial primary key,
    run_id             bigint not null references crawler_run(run_id),
    url                varchar(1024) not null,
    city               varchar(128) null,
    status             varchar(32) not null,
    attempt            integer not null default 0,
    max_attempts       integer not null,
    next_attempt_at    timestamptz not null default now(),
    reserved_at        timestamptz null,
    lease_until        timestamptz null,
    reserved_by        varchar(128) null,
    idempotency_key    varchar(128) not null,
    last_error_code    varchar(64) null,
    last_http_status   integer null,
    last_error_message varchar(512) null,
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    finished_at        timestamptz null
);

create table if not exists product (
    product_key   bigserial primary key,
    product_id    varchar(64) not null unique,
    name          varchar(512) not null,
    url           varchar(1024) not null,
    pack_value    numeric(18,6) null,
    pack_unit     varchar(16) null,
    created_at    timestamptz not null default now()
);

create table if not exists product_errors (
    product_key   bigserial primary key,
    queue_id      bigint null unique references price_collect_queue(queue_id),
    run_id        bigint null references crawler_run(run_id),
    product_id    varchar(64) null,
    name          varchar(512) not null,
    url           varchar(1024) not null,
    pack_value    numeric(18,6) null,
    pack_unit     varchar(16) null,
    created_at    timestamptz not null default now(),
    error_string  varchar(256) null,
    error_code    varchar(64) null,
    http_status   integer null,
    error_message varchar(512) null
);

create table if not exists price_snapshot (
    snapshot_id   bigserial primary key,
    queue_id      bigint null unique references price_collect_queue(queue_id),
    run_id        bigint not null references Crawler_run(run_id),
    captured_at   timestamptz not null default now(),
    product_key   bigint not null references product(product_key),
    city          varchar(128) null,
    price         numeric(18,2) not null,
    old_price     numeric(18,2) null,
    promo_flag    boolean not null default false,
    in_stock      boolean null
);

create index if not exists ix_price_snapshot_run on price_snapshot(run_id);
create index if not exists ix_price_snapshot_product_time on price_snapshot(product_key, captured_at);
create unique index if not exists ux_price_snapshot_queue_id on price_snapshot(queue_id);
create unique index if not exists ux_price_collect_queue_run_url on price_collect_queue(run_id, url);
create unique index if not exists ux_price_collect_queue_idempotency on price_collect_queue(idempotency_key);
create index if not exists ix_price_collect_queue_pick on price_collect_queue(status, next_attempt_at, queue_id);
create index if not exists ix_price_collect_queue_lease on price_collect_queue(status, lease_until);
create unique index if not exists ux_product_errors_queue_id on product_errors(queue_id);
create index if not exists ix_product_errors_product_id on product_errors(product_id);
create index if not exists ix_product_errors_run_id on product_errors(run_id);
create index if not exists ix_product_errors_error_code on product_errors(error_code);
create index if not exists ix_product_errors_created_at on product_errors(created_at);
