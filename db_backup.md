pg_dump -h localhost -p 5432 -U var -d varprice -F c -f "D:\Backups\Varus\Postgres\varus_2026-03-13.backup"
pg_restore -h localhost -p 5432 -U var -d varprice --clean --if-exists "D:\Backups\Varus\Postgres\varus_2026-03-09.backup"


THe working code
docker ps
docker exec -t 4b3db4684be1 pg_dump -U var -d varprice -F c -f /tmp/varus.backup
docker cp 4b3db4684be1:/tmp/varus.backup "D:\Backups\Varus\Postgres\varus-13-03-2026.backup"


RESTORE
docker cp "D:\Backups\Varus\Postgres\varus.backup" 4b3db4684be1:/tmp/varus.backup
docker exec -i 4b3db4684be1 pg_restore -U var -d varprice --clean --if-exists /tmp/varus.backup
