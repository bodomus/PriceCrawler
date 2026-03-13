pg_dump -h localhost -p 5432 -U var -d varprice -F c -f "D:\Backups\Varus\Postgres\varus_2026-03-09.backup"
pg_restore -h localhost -p 5432 -U var -d varprice --clean --if-exists "D:\Backups\Varus\Postgres\varus_2026-03-09.backup"

