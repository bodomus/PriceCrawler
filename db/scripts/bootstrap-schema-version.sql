-- Validates and registers an existing PriceCrawler database as schema version 1.
-- The script changes only public.schema_version. Application objects and data are read-only.
-- A Production-like database name requires this explicit session setting after backup:
--   SET pricecrawler.allow_production_bootstrap = 'true';

BEGIN;

DO $$
DECLARE
    expected record;
    actual_type text;
    actual_nullable text;
    actual_hash text;
    existing_count integer;
    existing_version integer;
    existing_migration_name text;
    existing_application_version text;
    existing_checksum text;
BEGIN
    IF current_database() ~* '(^|[_-])(prod|production)([_-]|$)'
       AND coalesce(current_setting('pricecrawler.allow_production_bootstrap', true), 'false') <> 'true' THEN
        RAISE EXCEPTION USING
            MESSAGE = format('Schema bootstrap is blocked for Production-like database %L.', current_database()),
            HINT = 'Create and verify the required backup, then explicitly SET pricecrawler.allow_production_bootstrap = ''true'' in this session.';
    END IF;

    FOREACH existing_migration_name IN ARRAY ARRAY[
        'crawler_run',
        'crawler_run_stage',
        'ingestion_run',
        'product',
        'product_catalog',
        'product_catalog_refresh',
        'price_collect_queue',
        'price_snapshot',
        'crawl_error',
        'db_routine_script'
    ]
    LOOP
        IF to_regclass(format('public.%I', existing_migration_name)) IS NULL THEN
            RAISE EXCEPTION 'Required PriceCrawler table public.% is missing.', existing_migration_name;
        END IF;
    END LOOP;

    FOR expected IN
        SELECT *
        FROM (VALUES
            ('crawl_error', 'id', 'bigint', 'NO'),
            ('crawl_error', 'run_id', 'bigint', 'NO'),
            ('crawl_error', 'queue_id', 'bigint', 'YES'),
            ('crawl_error', 'product_id', 'bigint', 'YES'),
            ('crawl_error', 'url', 'character varying(1024)', 'YES'),
            ('crawl_error', 'error_code', 'character varying(64)', 'YES'),
            ('crawl_error', 'http_status', 'integer', 'YES'),
            ('crawl_error', 'error_message', 'character varying(512)', 'YES'),
            ('crawl_error', 'created_at', 'timestamp with time zone', 'NO'),
            ('crawler_run', 'id', 'bigint', 'NO'),
            ('crawler_run', 'started_at', 'timestamp with time zone', 'NO'),
            ('crawler_run', 'finished_at', 'timestamp with time zone', 'YES'),
            ('crawler_run', 'status', 'character varying(32)', 'NO'),
            ('crawler_run', 'source', 'character varying(64)', 'NO'),
            ('crawler_run', 'note', 'character varying(255)', 'YES'),
            ('crawler_run', 'run_type', 'character varying(50)', 'NO'),
            ('crawler_run', 'discovery_source', 'character varying(50)', 'YES'),
            ('crawler_run', 'duration_ms', 'bigint', 'YES'),
            ('crawler_run', 'discovered_count', 'integer', 'NO'),
            ('crawler_run', 'accepted_count', 'integer', 'NO'),
            ('crawler_run', 'inserted_count', 'integer', 'NO'),
            ('crawler_run', 'updated_count', 'integer', 'NO'),
            ('crawler_run', 'reactivated_count', 'integer', 'NO'),
            ('crawler_run', 'deactivated_count', 'integer', 'NO'),
            ('crawler_run', 'selected_count', 'integer', 'NO'),
            ('crawler_run', 'enqueued_count', 'integer', 'NO'),
            ('crawler_run', 'succeeded_count', 'integer', 'NO'),
            ('crawler_run', 'retry_count', 'integer', 'NO'),
            ('crawler_run', 'dead_count', 'integer', 'NO'),
            ('crawler_run', 'failed_count', 'integer', 'NO'),
            ('crawler_run', 'products_created_count', 'integer', 'NO'),
            ('crawler_run', 'products_updated_count', 'integer', 'NO'),
            ('crawler_run', 'snapshots_created_count', 'integer', 'NO'),
            ('crawler_run', 'errors_created_count', 'integer', 'NO'),
            ('crawler_run', 'error_code', 'character varying(100)', 'YES'),
            ('crawler_run', 'error_message', 'character varying(1000)', 'YES'),
            ('crawler_run', 'created_at', 'timestamp with time zone', 'NO'),
            ('crawler_run', 'updated_at', 'timestamp with time zone', 'NO'),
            ('crawler_run_stage', 'id', 'bigint', 'NO'),
            ('crawler_run_stage', 'run_id', 'bigint', 'NO'),
            ('crawler_run_stage', 'stage', 'character varying(100)', 'NO'),
            ('crawler_run_stage', 'started_at', 'timestamp with time zone', 'NO'),
            ('crawler_run_stage', 'finished_at', 'timestamp with time zone', 'NO'),
            ('crawler_run_stage', 'duration_ms', 'bigint', 'NO'),
            ('crawler_run_stage', 'item_count', 'integer', 'YES'),
            ('crawler_run_stage', 'created_at', 'timestamp with time zone', 'NO'),
            ('db_routine_script', 'script_name', 'character varying(255)', 'NO'),
            ('db_routine_script', 'script_hash', 'character varying(64)', 'NO'),
            ('db_routine_script', 'applied_at', 'timestamp with time zone', 'NO'),
            ('ingestion_run', 'ingestion_run_id', 'bigint', 'NO'),
            ('ingestion_run', 'crawler_run_id', 'bigint', 'NO'),
            ('ingestion_run', 'started_at', 'timestamp with time zone', 'NO'),
            ('ingestion_run', 'finished_at', 'timestamp with time zone', 'YES'),
            ('ingestion_run', 'status', 'character varying(32)', 'NO'),
            ('ingestion_run', 'error_code', 'character varying(128)', 'YES'),
            ('ingestion_run', 'error_message', 'character varying(512)', 'YES'),
            ('price_collect_queue', 'id', 'bigint', 'NO'),
            ('price_collect_queue', 'run_id', 'bigint', 'NO'),
            ('price_collect_queue', 'product_catalog_id', 'bigint', 'YES'),
            ('price_collect_queue', 'url', 'character varying(1024)', 'NO'),
            ('price_collect_queue', 'page_kind', 'character varying(32)', 'NO'),
            ('price_collect_queue', 'status', 'character varying(32)', 'NO'),
            ('price_collect_queue', 'attempt', 'integer', 'NO'),
            ('price_collect_queue', 'max_attempts', 'integer', 'NO'),
            ('price_collect_queue', 'next_attempt_at', 'timestamp with time zone', 'YES'),
            ('price_collect_queue', 'reserved_at', 'timestamp with time zone', 'YES'),
            ('price_collect_queue', 'lease_until', 'timestamp with time zone', 'YES'),
            ('price_collect_queue', 'reserved_by', 'character varying(128)', 'YES'),
            ('price_collect_queue', 'idempotency_key', 'character varying(128)', 'YES'),
            ('price_collect_queue', 'last_error_code', 'character varying(64)', 'YES'),
            ('price_collect_queue', 'last_http_status', 'integer', 'YES'),
            ('price_collect_queue', 'last_error_message', 'character varying(512)', 'YES'),
            ('price_collect_queue', 'created_at', 'timestamp with time zone', 'NO'),
            ('price_collect_queue', 'updated_at', 'timestamp with time zone', 'YES'),
            ('price_collect_queue', 'finished_at', 'timestamp with time zone', 'YES'),
            ('price_snapshot', 'id', 'bigint', 'NO'),
            ('price_snapshot', 'run_id', 'bigint', 'NO'),
            ('price_snapshot', 'product_id', 'bigint', 'NO'),
            ('price_snapshot', 'captured_at', 'timestamp with time zone', 'NO'),
            ('price_snapshot', 'price', 'numeric(18,2)', 'YES'),
            ('price_snapshot', 'old_price', 'numeric(18,2)', 'YES'),
            ('price_snapshot', 'promo_flag', 'boolean', 'NO'),
            ('price_snapshot', 'in_stock', 'boolean', 'NO'),
            ('price_snapshot', 'queue_id', 'bigint', 'YES'),
            ('product', 'id', 'bigint', 'NO'),
            ('product', 'external_id', 'character varying(64)', 'YES'),
            ('product', 'name', 'character varying(512)', 'NO'),
            ('product', 'url', 'character varying(1024)', 'NO'),
            ('product', 'slug', 'character varying(512)', 'YES'),
            ('product', 'pack_value', 'numeric(18,6)', 'YES'),
            ('product', 'pack_unit', 'character varying(16)', 'YES'),
            ('product', 'created_at', 'timestamp with time zone', 'NO'),
            ('product', 'updated_at', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'id', 'bigint', 'NO'),
            ('product_catalog', 'source', 'character varying(50)', 'NO'),
            ('product_catalog', 'url', 'character varying(1024)', 'NO'),
            ('product_catalog', 'normalized_url', 'character varying(1024)', 'NO'),
            ('product_catalog', 'external_id', 'character varying(200)', 'YES'),
            ('product_catalog', 'slug', 'character varying(300)', 'YES'),
            ('product_catalog', 'first_discovered_at', 'timestamp with time zone', 'NO'),
            ('product_catalog', 'last_discovered_at', 'timestamp with time zone', 'NO'),
            ('product_catalog', 'last_checked_at', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'next_check_at', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'reserved_at', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'reserved_until', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'reserved_by', 'character varying(200)', 'YES'),
            ('product_catalog', 'is_active', 'boolean', 'NO'),
            ('product_catalog', 'consecutive_errors', 'integer', 'NO'),
            ('product_catalog', 'created_at', 'timestamp with time zone', 'NO'),
            ('product_catalog', 'updated_at', 'timestamp with time zone', 'NO'),
            ('product_catalog', 'last_seen_refresh_id', 'bigint', 'YES'),
            ('product_catalog', 'deactivated_at', 'timestamp with time zone', 'YES'),
            ('product_catalog', 'reactivated_at', 'timestamp with time zone', 'YES'),
            ('product_catalog_refresh', 'id', 'bigint', 'NO'),
            ('product_catalog_refresh', 'source', 'character varying(50)', 'NO'),
            ('product_catalog_refresh', 'discovery_source', 'character varying(50)', 'NO'),
            ('product_catalog_refresh', 'started_at', 'timestamp with time zone', 'NO'),
            ('product_catalog_refresh', 'finished_at', 'timestamp with time zone', 'YES'),
            ('product_catalog_refresh', 'status', 'character varying(20)', 'NO'),
            ('product_catalog_refresh', 'discovered_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'accepted_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'inserted_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'updated_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'deactivated_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'reactivated_count', 'integer', 'NO'),
            ('product_catalog_refresh', 'error_code', 'character varying(100)', 'YES'),
            ('product_catalog_refresh', 'error_message', 'character varying(1000)', 'YES'),
            ('product_catalog_refresh', 'created_at', 'timestamp with time zone', 'NO'),
            ('product_catalog_refresh', 'updated_at', 'timestamp with time zone', 'NO')
        ) AS columns(table_name, column_name, data_type, is_nullable)
    LOOP
        SELECT format_type(attribute.atttypid, attribute.atttypmod),
               CASE WHEN attribute.attnotnull THEN 'NO' ELSE 'YES' END
        INTO actual_type, actual_nullable
        FROM pg_catalog.pg_attribute attribute
        JOIN pg_catalog.pg_class relation ON relation.oid = attribute.attrelid
        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
        WHERE namespace.nspname = 'public'
          AND relation.relname = expected.table_name
          AND attribute.attname = expected.column_name
          AND attribute.attnum > 0
          AND NOT attribute.attisdropped;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Required column public.%.% is missing.', expected.table_name, expected.column_name;
        END IF;
        IF actual_type <> expected.data_type OR actual_nullable <> expected.is_nullable THEN
            RAISE EXCEPTION
                'Column public.%.% is incompatible. Expected type % nullable %, actual type % nullable %.',
                expected.table_name, expected.column_name, expected.data_type, expected.is_nullable,
                actual_type, actual_nullable;
        END IF;
    END LOOP;

    FOR expected IN
        SELECT *
        FROM (VALUES
            ('crawl_error', 'crawl_error_pkey', 'PRIMARY KEY (id)'),
            ('crawl_error', 'crawl_error_product_id_fkey', 'FOREIGN KEY (product_id) REFERENCES product(id)'),
            ('crawl_error', 'crawl_error_queue_id_fkey', 'FOREIGN KEY (queue_id) REFERENCES price_collect_queue(id)'),
            ('crawl_error', 'crawl_error_run_id_fkey', 'FOREIGN KEY (run_id) REFERENCES crawler_run(id)'),
            ('crawler_run', 'crawler_run_pkey', 'PRIMARY KEY (id)'),
            ('crawler_run_stage', 'crawler_run_stage_pkey', 'PRIMARY KEY (id)'),
            ('crawler_run_stage', 'crawler_run_stage_run_id_fkey', 'FOREIGN KEY (run_id) REFERENCES crawler_run(id) ON DELETE CASCADE'),
            ('crawler_run_stage', 'uq_crawler_run_stage', 'UNIQUE (run_id, stage)'),
            ('db_routine_script', 'db_routine_script_pkey', 'PRIMARY KEY (script_name)'),
            ('ingestion_run', 'ingestion_run_crawler_run_id_fkey', 'FOREIGN KEY (crawler_run_id) REFERENCES crawler_run(id)'),
            ('ingestion_run', 'ingestion_run_pkey', 'PRIMARY KEY (ingestion_run_id)'),
            ('price_collect_queue', 'fk_price_collect_queue_product_catalog', 'FOREIGN KEY (product_catalog_id) REFERENCES product_catalog(id) ON DELETE RESTRICT'),
            ('price_collect_queue', 'price_collect_queue_pkey', 'PRIMARY KEY (id)'),
            ('price_collect_queue', 'price_collect_queue_run_id_fkey', 'FOREIGN KEY (run_id) REFERENCES crawler_run(id)'),
            ('price_snapshot', 'price_snapshot_pkey', 'PRIMARY KEY (id)'),
            ('price_snapshot', 'price_snapshot_product_id_fkey', 'FOREIGN KEY (product_id) REFERENCES product(id)'),
            ('price_snapshot', 'price_snapshot_queue_id_fkey', 'FOREIGN KEY (queue_id) REFERENCES price_collect_queue(id)'),
            ('price_snapshot', 'price_snapshot_run_id_fkey', 'FOREIGN KEY (run_id) REFERENCES crawler_run(id)'),
            ('product', 'product_pkey', 'PRIMARY KEY (id)'),
            ('product_catalog', 'fk_product_catalog_last_seen_refresh', 'FOREIGN KEY (last_seen_refresh_id) REFERENCES product_catalog_refresh(id) ON DELETE RESTRICT'),
            ('product_catalog', 'product_catalog_pkey', 'PRIMARY KEY (id)'),
            ('product_catalog_refresh', 'product_catalog_refresh_pkey', 'PRIMARY KEY (id)')
        ) AS constraints(table_name, constraint_name, definition)
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM pg_catalog.pg_constraint constraint_record
            JOIN pg_catalog.pg_class relation ON relation.oid = constraint_record.conrelid
            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = 'public'
              AND relation.relname = expected.table_name
              AND constraint_record.conname = expected.constraint_name
              AND pg_get_constraintdef(constraint_record.oid) = expected.definition
        ) THEN
            RAISE EXCEPTION 'Required constraint %.% is missing or incompatible.',
                expected.table_name, expected.constraint_name;
        END IF;
    END LOOP;

    FOR expected IN
        SELECT *
        FROM (VALUES
            ('ix_crawl_error_product_id', false),
            ('ix_crawl_error_run_id', false),
            ('ix_crawler_run_run_type_started_at', false),
            ('ix_crawler_run_source_started_at_desc', false),
            ('ix_crawler_run_started_at', false),
            ('ix_crawler_run_status_started_at', false),
            ('ix_crawler_run_stage_run_id', false),
            ('ix_price_collect_queue_lease', false),
            ('ix_price_collect_queue_pick', false),
            ('ix_price_collect_queue_product_catalog_id', false),
            ('ux_price_collect_queue_idempotency', true),
            ('ux_price_collect_queue_run_url', true),
            ('ix_price_snapshot_product_captured_at_desc', false),
            ('ix_price_snapshot_run_id', false),
            ('ix_product_external_id', false),
            ('ux_product_url', true),
            ('ix_product_catalog_due', false),
            ('ix_product_catalog_last_discovered_at', false),
            ('ix_product_catalog_last_seen_refresh_id', false),
            ('ix_product_catalog_reservation', false),
            ('ux_product_catalog_source_normalized_url', true),
            ('ux_product_catalog_refresh_running_source', true)
        ) AS indexes(index_name, is_unique)
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM pg_catalog.pg_index index_record
            JOIN pg_catalog.pg_class index_relation ON index_relation.oid = index_record.indexrelid
            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = index_relation.relnamespace
            WHERE namespace.nspname = 'public'
              AND index_relation.relname = expected.index_name
              AND index_record.indisunique = expected.is_unique
              AND index_record.indisvalid
        ) THEN
            RAISE EXCEPTION 'Required index public.% is missing or incompatible.', expected.index_name;
        END IF;
    END LOOP;

    IF position(
        '(is_active, next_check_at, reserved_until, last_checked_at, id)'
        IN pg_get_indexdef('public.ix_product_catalog_due'::regclass)) = 0 THEN
        RAISE EXCEPTION 'Critical index public.ix_product_catalog_due has incompatible columns.';
    END IF;

    FOR expected IN
        SELECT *
        FROM (VALUES
            ('crawl_error_add(p_run_id bigint, p_queue_id bigint, p_product_id bigint, p_url text, p_created_at timestamp with time zone, p_error_code text, p_http_status integer, p_error_message text)'),
            ('crawler_run_complete(IN p_run_id bigint, IN p_status text, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_reactivated_count integer, IN p_deactivated_count integer, IN p_selected_count integer, IN p_enqueued_count integer, IN p_succeeded_count integer, IN p_retry_count integer, IN p_dead_count integer, IN p_failed_count integer, IN p_products_created_count integer, IN p_products_updated_count integer, IN p_snapshots_created_count integer, IN p_errors_created_count integer, IN p_stages_json text, IN p_note text, IN p_error_code text, IN p_error_message text)'),
            ('crawler_run_finish(IN p_run_id bigint, IN p_status text, IN p_note text)'),
            ('crawler_run_get_aggregate(p_from timestamp with time zone, p_to timestamp with time zone, p_run_type text)'),
            ('crawler_run_get_by_id(p_run_id bigint)'),
            ('crawler_run_get_recent(p_limit integer, p_run_type text, p_status text)'),
            ('crawler_run_stage_get(p_run_id bigint)'),
            ('crawler_run_start(p_run_type text, p_source text, p_discovery_source text)'),
            ('crawler_run_start(p_source text)'),
            ('ingestion_run_finish(IN p_ingestion_run_id bigint, IN p_status text, IN p_error_code text, IN p_error_message text)'),
            ('ingestion_run_start(p_crawler_run_id bigint)'),
            ('price_collect_queue_enqueue(p_run_id bigint, p_urls text[], p_idempotency_keys text[], p_max_attempts integer, p_product_catalog_ids bigint[], p_page_kinds text[])'),
            ('price_collect_queue_enqueue_result(p_run_id bigint, p_urls text[], p_idempotency_keys text[], p_max_attempts integer, p_product_catalog_ids bigint[], p_page_kinds text[])'),
            ('price_collect_queue_get_run_stats(p_run_id bigint)'),
            ('price_collect_queue_has_outstanding(p_run_id bigint)'),
            ('price_collect_queue_mark_dead(IN p_queue_id bigint, IN p_error_code text, IN p_http_status integer, IN p_error_message text)'),
            ('price_collect_queue_mark_retry(IN p_queue_id bigint, IN p_error_code text, IN p_http_status integer, IN p_error_message text, IN p_next_attempt_at timestamp with time zone)'),
            ('price_collect_queue_mark_succeeded(IN p_queue_id bigint)'),
            ('price_collect_queue_reap_expired(p_run_id bigint)'),
            ('price_collect_queue_reserve_batch(p_run_id bigint, p_batch_size integer, p_worker_id text, p_lease_seconds integer)'),
            ('price_observation_store(p_run_id bigint, p_queue_id bigint, p_external_id text, p_name text, p_url text, p_slug text, p_pack_value numeric, p_pack_unit text, p_price numeric, p_old_price numeric, p_promo_flag boolean, p_in_stock boolean, p_observed_at timestamp with time zone)'),
            ('product_catalog_deactivate_missing(p_source text, p_current_refresh_id bigint, p_not_seen_since timestamp with time zone, p_deactivated_at timestamp with time zone)'),
            ('product_catalog_get_active_count(p_source text)'),
            ('product_catalog_get_by_id(p_id bigint)'),
            ('product_catalog_get_by_source_normalized_url(p_source text, p_normalized_url text)'),
            ('product_catalog_get_due(p_limit integer, p_now timestamp with time zone, p_lease_seconds integer, p_worker_id text)'),
            ('product_catalog_mark_checked(IN p_catalog_item_id bigint, IN p_checked_at timestamp with time zone, IN p_next_check_at timestamp with time zone, IN p_external_id text, IN p_slug text)'),
            ('product_catalog_mark_failed(IN p_catalog_item_id bigint, IN p_attempted_at timestamp with time zone, IN p_next_check_at timestamp with time zone)'),
            ('product_catalog_refresh_complete(IN p_refresh_id bigint, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_deactivated_count integer, IN p_reactivated_count integer, IN p_finished_at timestamp with time zone)'),
            ('product_catalog_refresh_complete_with_run(IN p_refresh_id bigint, IN p_run_id bigint, IN p_discovered_count integer, IN p_accepted_count integer, IN p_inserted_count integer, IN p_updated_count integer, IN p_deactivated_count integer, IN p_reactivated_count integer, IN p_finished_at timestamp with time zone, IN p_run_note text)'),
            ('product_catalog_refresh_fail(IN p_refresh_id bigint, IN p_status text, IN p_error_code text, IN p_error_message text, IN p_finished_at timestamp with time zone)'),
            ('product_catalog_refresh_fail_with_run(IN p_refresh_id bigint, IN p_run_id bigint, IN p_status text, IN p_error_code text, IN p_error_message text, IN p_finished_at timestamp with time zone, IN p_run_status text, IN p_run_note text)'),
            ('product_catalog_refresh_get_by_id(p_refresh_id bigint)'),
            ('product_catalog_refresh_start(p_source text, p_discovery_source text, p_started_at timestamp with time zone, p_abandoned_before timestamp with time zone)'),
            ('product_catalog_release_reservations(p_catalog_item_ids bigint[])'),
            ('product_catalog_upsert_discovered(p_refresh_id bigint, p_items text)'),
            ('routine_support_queue_status(p_status text)'),
            ('routine_support_run_status(p_status text)'),
            ('routine_support_trim_nullable(p_value text, p_max_length integer)'),
            ('routine_support_trim_required(p_value text, p_max_length integer)')
        ) AS routines(identity)
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM pg_catalog.pg_proc routine
            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
            WHERE namespace.nspname = 'public'
              AND routine.proname || '(' || pg_get_function_identity_arguments(routine.oid) || ')' = expected.identity
        ) THEN
            RAISE EXCEPTION 'Required routine public.% is missing.', expected.identity;
        END IF;
    END LOOP;

    FOR expected IN
        SELECT *
        FROM (VALUES
            ('001__routine_support_text.sql', '8eb7461e125e5c948bcea9f41428af79fc8df3325233a0e66cfb95195a52a9ae'),
            ('010__run_error_routines.sql', '662dc24862ecd0cc5e5d28361fb9521dca2310d25d7d76fd2b1670ec81f01cd0'),
            ('020__queue_routines.sql', '95e4a28002237526b90866d7910f5a9b5ac9a5ae3b49d23ae36807fd28f3fbbd'),
            ('030__observation_routines.sql', 'e1e536a69ad829f932df2ab93e2612b8077677add9f6702aa1fd8cd1b4511c69'),
            ('040__product_catalog_routines.sql', '365f7729f69d1f58494f1f1590edb69d3547467fd0af11a0f06b32858983e365'),
            ('050__crawler_run_statistics.sql', '81cd1ee85e56336aceba9c3f4f3c7bf69be300cf8341740a52c1b65d3453cc0c')
        ) AS scripts(script_name, script_hash)
    LOOP
        SELECT script_hash INTO actual_hash
        FROM public.db_routine_script
        WHERE script_name = expected.script_name;
        IF NOT FOUND OR actual_hash <> expected.script_hash THEN
            RAISE EXCEPTION 'Routine metadata for % is missing or incompatible.', expected.script_name;
        END IF;
    END LOOP;

    IF to_regclass('public.schema_version') IS NOT NULL THEN
        IF EXISTS (
            SELECT required.column_name
            FROM (VALUES
                ('version', 'integer', 'NO'),
                ('migration_name', 'character varying(200)', 'NO'),
                ('applied_at_utc', 'timestamp with time zone', 'NO'),
                ('application_version', 'character varying(50)', 'YES'),
                ('checksum', 'character varying(128)', 'YES')
            ) AS required(column_name, data_type, is_nullable)
            LEFT JOIN (
                SELECT attribute.attname AS column_name,
                       format_type(attribute.atttypid, attribute.atttypmod) AS data_type,
                       CASE WHEN attribute.attnotnull THEN 'NO' ELSE 'YES' END AS is_nullable
                FROM pg_catalog.pg_attribute attribute
                WHERE attribute.attrelid = 'public.schema_version'::regclass
                  AND attribute.attnum > 0
                  AND NOT attribute.attisdropped
            ) actual USING (column_name, data_type, is_nullable)
            WHERE actual.column_name IS NULL
        ) THEN
            RAISE EXCEPTION 'Existing public.schema_version table has an incompatible structure.';
        END IF;

        SELECT count(*), max(version), max(migration_name), max(application_version), max(checksum)
        INTO existing_count, existing_version, existing_migration_name,
             existing_application_version, existing_checksum
        FROM public.schema_version;

        IF existing_count <> 1
           OR existing_version <> 1
           OR existing_migration_name <> '0001_baseline'
           OR existing_application_version <> 'v0.4.1-alpha'
           OR existing_checksum IS NOT NULL THEN
            RAISE EXCEPTION 'Existing schema version metadata conflicts with version 1 baseline registration.';
        END IF;

        RAISE NOTICE 'PriceCrawler database is already registered as schema version 1.';
    END IF;
END;
$$;

CREATE TABLE IF NOT EXISTS public.schema_version
(
    version integer NOT NULL PRIMARY KEY,
    migration_name character varying(200) NOT NULL,
    applied_at_utc timestamp with time zone NOT NULL DEFAULT now(),
    application_version character varying(50),
    checksum character varying(128)
);

INSERT INTO public.schema_version(version, migration_name, application_version, checksum)
VALUES (1, '0001_baseline', 'v0.4.1-alpha', NULL)
ON CONFLICT (version) DO NOTHING;

COMMIT;

