using System.Data;

namespace VarPrice.Web.Storage;

public sealed class SchemaBootstrapper(IPgConnectionFactory factory, ILogger<SchemaBootstrapper> log)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const int attempts = 30;
        const int delayMs = 1000;

        Exception? last = null;

        for (var i = 1; i <= attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var cn = factory.Create();
                cn.Open();

                foreach (var sql in GetStatements())
                {
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                }

                log.LogInformation("Schema ensured");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                log.LogWarning(ex, "Postgres not ready (attempt {Attempt}/{Attempts})", i, attempts);
                await Task.Delay(delayMs, ct);
            }
        }

        throw new InvalidOperationException("Failed to ensure schema (Postgres not ready)", last);
    }

    private static IEnumerable<string> GetStatements() => new[]
    {
        @"create table if not exists Crawler_run (
            run_id        bigserial primary key,
            started_at    timestamptz not null default now(),
            finished_at   timestamptz null,
            status        varchar(32) not null,
            source        varchar(64) not null,
            note          varchar(255) null
        );",

        @"create table if not exists product (
            product_key   bigserial primary key,
            product_id    varchar(64) not null unique,
            name          varchar(512) not null,
            url           varchar(1024) not null,
            pack_value    numeric(18,6) null,
            pack_unit     varchar(16) null,
            created_at    timestamptz not null default now()
        );",

        @"create table if not exists ingestion_run (
            run_id           bigserial primary key,
            crawler_run_id   bigint not null references Crawler_run(run_id),
            started_at       timestamptz not null default now(),
            finished_at      timestamptz null,
            status           varchar(32) not null,
            source           varchar(64) not null,
            note             varchar(255) null,
            error_message    varchar(2048) null,
            error_details    text null,
            error_source     varchar(128) null,
            error_at         timestamptz null
        );",

        @"alter table ingestion_run add column if not exists crawler_run_id bigint;",
        @"alter table ingestion_run add column if not exists started_at timestamptz;",
        @"alter table ingestion_run add column if not exists finished_at timestamptz;",
        @"alter table ingestion_run add column if not exists status varchar(32);",
        @"alter table ingestion_run add column if not exists source varchar(64);",
        @"alter table ingestion_run add column if not exists note varchar(255);",
        @"alter table ingestion_run add column if not exists error_message varchar(2048);",
        @"alter table ingestion_run add column if not exists error_details text;",
        @"alter table ingestion_run add column if not exists error_source varchar(128);",
        @"alter table ingestion_run add column if not exists error_at timestamptz;",

        @"create table if not exists price_snapshot (
            snapshot_id   bigserial primary key,
            run_id        bigint not null references ingestion_run(run_id),
            captured_at   timestamptz not null default now(),
            product_key   bigint not null references product(product_key),
            city          varchar(64) null,
            price         numeric(18,2) not null,
            old_price     numeric(18,2) null,
            promo_flag    boolean not null default false,
            in_stock      boolean null
        );",

        @"insert into Crawler_run(run_id, started_at, finished_at, status, source, note)
        select distinct
            ps.run_id,
            now(),
            now(),
            'legacy',
            'schema-migration',
            'created for ingestion_run migration'
        from price_snapshot ps
        left join Crawler_run cr on cr.run_id = ps.run_id
        where cr.run_id is null;",

        @"insert into Crawler_run(run_id, started_at, finished_at, status, source, note)
        select distinct
            ir.run_id,
            coalesce(ir.started_at, now()),
            ir.finished_at,
            coalesce(nullif(ir.status, ''), 'legacy'),
            coalesce(nullif(ir.source, ''), 'schema-migration'),
            coalesce(ir.note, 'created for ingestion_run migration')
        from ingestion_run ir
        left join Crawler_run cr on cr.run_id = ir.run_id
        where ir.crawler_run_id is null
          and cr.run_id is null;",

        @"update ingestion_run
        set crawler_run_id = run_id
        where crawler_run_id is null;",

        @"insert into Crawler_run(run_id, started_at, finished_at, status, source, note)
        select distinct
            ir.crawler_run_id,
            now(),
            now(),
            'legacy',
            'schema-migration',
            'created for ingestion_run migration'
        from ingestion_run ir
        left join Crawler_run cr on cr.run_id = ir.crawler_run_id
        where ir.crawler_run_id is not null
          and cr.run_id is null;",

        @"update ingestion_run
        set started_at = coalesce(started_at, now()),
            status = coalesce(nullif(status, ''), 'running'),
            source = coalesce(nullif(source, ''), 'unknown')
        where started_at is null
           or status is null
           or status = ''
           or source is null
           or source = '';",

        @"insert into ingestion_run(run_id, crawler_run_id, started_at, finished_at, status, source, note)
        select distinct
            ps.run_id,
            cr.run_id,
            coalesce(cr.started_at, now()),
            cr.finished_at,
            coalesce(nullif(cr.status, ''), 'legacy'),
            coalesce(nullif(cr.source, ''), 'schema-migration'),
            coalesce(cr.note, 'migrated from crawler_run')
        from price_snapshot ps
        join Crawler_run cr on cr.run_id = ps.run_id
        left join ingestion_run ir on ir.run_id = ps.run_id
        where ir.run_id is null;",

        @"alter table ingestion_run alter column started_at set default now();",
        @"alter table ingestion_run alter column started_at set not null;",
        @"alter table ingestion_run alter column status set not null;",
        @"alter table ingestion_run alter column source set not null;",
        @"alter table ingestion_run alter column crawler_run_id set not null;",

        @"do $$
begin
    if not exists (
        select 1
        from pg_constraint c
        join pg_class t on t.oid = c.conrelid
        join pg_class rt on rt.oid = c.confrelid
        where t.relname = 'ingestion_run'
          and c.conname = 'ingestion_run_crawler_run_id_fkey'
          and rt.relname = 'crawler_run'
    ) then
        alter table ingestion_run drop constraint if exists ingestion_run_crawler_run_id_fkey;
        alter table ingestion_run
            add constraint ingestion_run_crawler_run_id_fkey
            foreign key (crawler_run_id) references crawler_run(run_id);
    end if;
end $$;",

        @"alter table price_snapshot alter column run_id set not null;",

        @"do $$
begin
    if exists (
        select 1
        from pg_constraint c
        join pg_class t on t.oid = c.conrelid
        where t.relname = 'price_snapshot'
          and c.conname = 'price_snapshot_run_id_fkey'
    ) and not exists (
        select 1
        from pg_constraint c
        join pg_class t on t.oid = c.conrelid
        join pg_class rt on rt.oid = c.confrelid
        where t.relname = 'price_snapshot'
          and c.conname = 'price_snapshot_run_id_fkey'
          and rt.relname = 'ingestion_run'
    ) then
        alter table price_snapshot drop constraint price_snapshot_run_id_fkey;
    end if;

    if not exists (
        select 1
        from pg_constraint c
        join pg_class t on t.oid = c.conrelid
        join pg_class rt on rt.oid = c.confrelid
        where t.relname = 'price_snapshot'
          and c.conname = 'price_snapshot_run_id_fkey'
          and rt.relname = 'ingestion_run'
    ) then
        alter table price_snapshot
            add constraint price_snapshot_run_id_fkey
            foreign key (run_id) references ingestion_run(run_id);
    end if;
end $$;",

        @"create index if not exists ix_ingestion_run_crawler_run_id on ingestion_run(crawler_run_id);",
        @"create index if not exists ix_price_snapshot_run on price_snapshot(run_id);",
        @"create index if not exists ix_price_snapshot_product_time on price_snapshot(product_key, captured_at);",

        @"do $$
declare
    seq_name text;
begin
    seq_name := pg_get_serial_sequence('crawler_run', 'run_id');
    if seq_name is not null then
        execute format(
            'select setval(%L, %s, %s);',
            seq_name,
            coalesce((select max(run_id)::text from crawler_run), '1'),
            case when exists(select 1 from crawler_run) then 'true' else 'false' end);
    end if;
end $$;",

        @"do $$
declare
    seq_name text;
begin
    seq_name := pg_get_serial_sequence('ingestion_run', 'run_id');
    if seq_name is not null then
        execute format(
            'select setval(%L, %s, %s);',
            seq_name,
            coalesce((select max(run_id)::text from ingestion_run), '1'),
            case when exists(select 1 from ingestion_run) then 'true' else 'false' end);
    end if;
end $$;"
    };
}
