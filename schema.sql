create table if not exists Crawler_run (
    run_id        bigserial primary key,
    started_at    timestamptz not null default now(),
    finished_at   timestamptz null,
    status        varchar(32) not null,
    source        varchar(64) not null,
    note          varchar(255) null
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

create table if not exists price_snapshot (
    snapshot_id   bigserial primary key,
    run_id        bigint not null references Crawler_run(run_id),
    captured_at   timestamptz not null default now(),
    product_key   bigint not null references product(product_key),
    city          varchar(64) null,
    price         numeric(18,2) not null,
    old_price     numeric(18,2) null,
    promo_flag    boolean not null default false,
    in_stock      boolean null
);

create index if not exists ix_price_snapshot_run on price_snapshot(run_id);
create index if not exists ix_price_snapshot_product_time on price_snapshot(product_key, captured_at);
