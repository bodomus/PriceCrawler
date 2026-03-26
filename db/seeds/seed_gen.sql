-- =========================================
-- VARUS test data generator for 1 month
-- PostgreSQL
-- =========================================

BEGIN;

-- -----------------------------------------
-- 0. Параметры генерации
-- -----------------------------------------
DO $$
BEGIN
    RAISE NOTICE 'Starting test data generation...';
END $$;

-- -----------------------------------------
-- 1. Временная таблица с настройками
-- -----------------------------------------
DROP TABLE IF EXISTS _seed_config;
CREATE TEMP TABLE _seed_config
(
    products_count      int,
    days_count          int,
    source_name         varchar(64),
    category_name       varchar(128),
    brand_count         int
);

INSERT INTO _seed_config(products_count, days_count, source_name, category_name, brand_count)
VALUES
(
    300,                -- сколько товаров создать
    30,                 -- глубина истории в днях
    'Worker_Varus',
    'Vegetables',
    12
);

-- -----------------------------------------
-- 2. Создаем crawler_run за каждый день
-- -----------------------------------------
-- Предполагаем структуру:
-- crawler_run(
--   run_id bigserial PK,
--   started_at timestamptz,
--   finished_at timestamptz,
--   status varchar(32),
--   source varchar(64),
--   note varchar(255)
-- )

WITH cfg AS (
    SELECT * FROM _seed_config
),
days AS (
    SELECT generate_series(
        (CURRENT_DATE - ((SELECT days_count FROM cfg) - 1) * INTERVAL '1 day')::date,
        CURRENT_DATE::date,
        INTERVAL '1 day'
    )::date AS day_date
)
INSERT INTO crawler_run (started_at, finished_at, status, source, note)
SELECT
    d.day_date
        + make_interval(hours => 6 + (random() * 10)::int, mins => (random() * 59)::int),
    d.day_date
        + make_interval(hours => 6 + (random() * 10)::int, mins => (random() * 59)::int)
        + make_interval(mins => 5 + (random() * 35)::int),
    CASE
        WHEN random() < 0.92 THEN 'OK'
        WHEN random() < 0.97 THEN 'WARNING'
        ELSE 'FAILED'
    END,
    (SELECT source_name FROM cfg),
    'Seeded monthly test run'
FROM days d;

-- -----------------------------------------
-- 3. Создаем тестовые товары
-- -----------------------------------------
-- Предполагаем структуру:
-- product(
--   product_id bigserial PK,
--   source varchar(64),
--   external_id varchar(128),
--   name varchar(255),
--   category varchar(128),
--   brand varchar(128),
--   is_active boolean,
--   created_at timestamptz,
--   updated_at timestamptz
-- )

WITH cfg AS (
    SELECT * FROM _seed_config
),
src AS (
    SELECT
        gs AS n,
        (SELECT source_name FROM cfg) AS source_name,
        (SELECT category_name FROM cfg) AS category_name,
        'Brand_' || (1 + floor(random() * (SELECT brand_count FROM cfg)))::int AS brand_name,
        md5(random()::text || clock_timestamp()::text) AS hash_value
    FROM generate_series(1, (SELECT products_count FROM cfg)) gs
)
INSERT INTO product
(
    source,
    external_id,
    name,
    category,
    brand,
    is_active,
    created_at,
    updated_at
)
SELECT
    source_name,
    'VARUS-' || lpad(n::text, 6, '0'),
    CASE (1 + floor(random() * 8))::int
        WHEN 1 THEN 'Картопля'
        WHEN 2 THEN 'Морква'
        WHEN 3 THEN 'Цибуля'
        WHEN 4 THEN 'Помідори'
        WHEN 5 THEN 'Огірки'
        WHEN 6 THEN 'Капуста'
        WHEN 7 THEN 'Буряк'
        ELSE 'Перець'
    END
    || ' '
    || CASE (1 + floor(random() * 6))::int
        WHEN 1 THEN 'свіжий'
        WHEN 2 THEN 'фасований'
        WHEN 3 THEN 'преміум'
        WHEN 4 THEN 'домашній'
        WHEN 5 THEN 'відбірний'
        ELSE 'економ'
    END
    || ' '
    || n,
    category_name,
    brand_name,
    TRUE,
    now(),
    now()
FROM src;

-- -----------------------------------------
-- 4. Генерация истории цен за месяц
-- -----------------------------------------
-- Предполагаем структуру:
-- price_snapshot(
--   snapshot_id bigserial PK,
--   product_id bigint,
--   crawler_run_id bigint,
--   price numeric(10,2),
--   old_price numeric(10,2),
--   in_stock boolean,
--   collected_at timestamptz,
--   created_at timestamptz
-- )

-- Базовая идея:
-- для каждого товара берем стартовую цену,
-- дальше по каждому дню слегка двигаем цену,
-- иногда делаем old_price > price, имитируя скидку,
-- иногда выключаем наличие.

WITH cfg AS (
    SELECT * FROM _seed_config
),
products_seed AS (
    SELECT
        p.product_id,
        p.name,
        round((20 + random() * 180)::numeric, 2) AS base_price
    FROM product p
    WHERE p.source = (SELECT source_name FROM cfg)
),
days AS (
    SELECT
        generate_series(
            (CURRENT_DATE - ((SELECT days_count FROM cfg) - 1) * INTERVAL '1 day')::date,
            CURRENT_DATE::date,
            INTERVAL '1 day'
        )::date AS day_date
),
runs AS (
    SELECT
        run_id,
        started_at::date AS run_date
    FROM crawler_run
    WHERE source = (SELECT source_name FROM cfg)
      AND note = 'Seeded monthly test run'
),
price_matrix AS (
    SELECT
        ps.product_id,
        ps.name,
        d.day_date,
        r.run_id,
        ps.base_price,
        -- легкий дневной дрейф цены
        round(
            greatest(
                5,
                ps.base_price
                + ((extract(day from d.day_date)::int % 7) - 3) * (0.3 + random() * 1.5)
                + ((random() - 0.5) * 4.0)
            )::numeric,
            2
        ) AS daily_price,
        random() AS promo_roll,
        random() AS stock_roll
    FROM products_seed ps
    CROSS JOIN days d
    JOIN runs r
      ON r.run_date = d.day_date
)
INSERT INTO price_snapshot
(
    product_id,
    crawler_run_id,
    price,
    old_price,
    in_stock,
    collected_at,
    created_at
)
SELECT
    pm.product_id,
    pm.run_id,
    CASE
        WHEN pm.promo_roll < 0.18
            THEN round((pm.daily_price * (0.85 + random() * 0.08))::numeric, 2)
        ELSE pm.daily_price
    END AS price,
    CASE
        WHEN pm.promo_roll < 0.18
            THEN pm.daily_price
        ELSE NULL
    END AS old_price,
    CASE
        WHEN pm.stock_roll < 0.07 THEN FALSE
        ELSE TRUE
    END AS in_stock,
    pm.day_date
        + make_interval(hours => 8 + (random() * 12)::int, mins => (random() * 59)::int),
    now()
FROM price_matrix pm;

-- -----------------------------------------
-- 5. Обновляем updated_at у product
-- -----------------------------------------
UPDATE product
SET updated_at = now()
WHERE source = (SELECT source_name FROM _seed_config LIMIT 1);

COMMIT;

-- -----------------------------------------
-- 6. Проверка результата
-- -----------------------------------------
SELECT 'crawler_run'  AS table_name, count(*) AS rows_count
FROM crawler_run
WHERE note = 'Seeded monthly test run'

UNION ALL

SELECT 'product' AS table_name, count(*) AS rows_count
FROM product
WHERE source = (SELECT source_name FROM _seed_config LIMIT 1)

UNION ALL

SELECT 'price_snapshot' AS table_name, count(*) AS rows_count
FROM price_snapshot ps
JOIN product p ON p.product_id = ps.product_id
WHERE p.source = (SELECT source_name FROM _seed_config LIMIT 1);