-- PriceCrawler schema version 2: per-store category tree (MPC-96).
-- Forward-only. Requires schema version 1 and is applied exactly once:
--   * Development/Test: automatically by DatabaseSchemaInitializer (StartupMode=Ensure);
--   * the working database: manually with psql after a verified backup (docs/collector.md).

BEGIN;

DO $$
DECLARE
    v_current integer;
BEGIN
    IF to_regclass('public.schema_version') IS NULL THEN
        RAISE EXCEPTION 'PriceCrawler migration 0002 requires schema_version metadata.';
    END IF;

    SELECT COALESCE(MAX(version), 0) INTO v_current FROM public.schema_version;
    IF v_current <> 1 THEN
        RAISE EXCEPTION USING
            MESSAGE = format('PriceCrawler migration 0002 requires schema version 1, found %s.', v_current),
            HINT = 'Apply forward migrations in order and never re-run an applied migration.';
    END IF;
END;
$$;

-- One row per category of one store. The tree is data, not code: catalog-refresh
-- re-reads it from the store site, adds new nodes, re-parents moved ones and
-- deactivates vanished ones (rows are never deleted, so history keeps its links).
CREATE TABLE public.category (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source character varying(50) NOT NULL,
    external_id character varying(300) NOT NULL,
    parent_id bigint NULL,
    name character varying(300) NOT NULL,
    url character varying(1024) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    first_seen_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    last_seen_refresh_id bigint NULL,
    deactivated_at timestamp with time zone NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT fk_category_parent FOREIGN KEY (parent_id) REFERENCES public.category(id) ON DELETE RESTRICT,
    CONSTRAINT fk_category_last_seen_refresh FOREIGN KEY (last_seen_refresh_id)
        REFERENCES public.product_catalog_refresh(id) ON DELETE RESTRICT,
    CONSTRAINT ck_category_not_own_parent CHECK (parent_id IS NULL OR parent_id <> id),
    CONSTRAINT ck_category_text_not_blank CHECK (
        btrim(source) <> '' AND btrim(external_id) <> '' AND btrim(name) <> '' AND btrim(url) <> '')
);

CREATE UNIQUE INDEX ux_category_source_external_id ON public.category USING btree (source, external_id);
CREATE INDEX ix_category_source_parent_sort ON public.category USING btree (source, parent_id, sort_order, id);

COMMENT ON TABLE public.category IS 'Per-store category tree refreshed by catalog-refresh (MPC-96).';
COMMENT ON COLUMN public.category.external_id IS 'Stable key of the category on the store site; for Varus the URL path, e.g. "banani".';

-- Which catalog items a category page listed. Only the categories that were
-- crawled are linked; ancestors get their products through the tree.
CREATE TABLE public.product_catalog_category (
    product_catalog_id bigint NOT NULL,
    category_id bigint NOT NULL,
    first_seen_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    last_seen_refresh_id bigint NULL,
    CONSTRAINT pk_product_catalog_category PRIMARY KEY (product_catalog_id, category_id),
    CONSTRAINT fk_product_catalog_category_catalog FOREIGN KEY (product_catalog_id)
        REFERENCES public.product_catalog(id) ON DELETE CASCADE,
    CONSTRAINT fk_product_catalog_category_category FOREIGN KEY (category_id)
        REFERENCES public.category(id) ON DELETE CASCADE,
    CONSTRAINT fk_product_catalog_category_refresh FOREIGN KEY (last_seen_refresh_id)
        REFERENCES public.product_catalog_refresh(id) ON DELETE RESTRICT
);

CREATE INDEX ix_product_catalog_category_category ON public.product_catalog_category
    USING btree (category_id, product_catalog_id);

INSERT INTO public.schema_version(version, migration_name, application_version, checksum)
VALUES (2, '0002_category_tree', NULL, NULL);

COMMIT;
