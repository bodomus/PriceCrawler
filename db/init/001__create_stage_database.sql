-- Runs only when the PostgreSQL data directory is initialized for the first time.
-- Creates the independent stage database next to the default development database.

SELECT 'CREATE DATABASE varprice_stage'
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_database
    WHERE datname = 'varprice_stage'
)\gexec
