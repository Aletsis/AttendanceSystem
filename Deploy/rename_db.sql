-- Aparta la base de datos de la aplicación para crear una nueva, sin borrar nada: la renombra.
-- Ejecutar como superusuario (postgres) con psql:
--   psql -w -v ON_ERROR_STOP=1 -U postgres -d postgres -v old_name=AttendanceSystem -v new_name=<nombre> -f rename_db.sql
-- Si old_name no existe no hace nada. Requiere PostgreSQL 14+ (pg_terminate_backend con espera).

-- 1. Impedir conexiones nuevas (el sitio, el servicio o el programa de escritorio podrían reconectarse)
SELECT format('ALTER DATABASE %I WITH ALLOW_CONNECTIONS false', :'old_name')
WHERE EXISTS (SELECT FROM pg_database WHERE datname = :'old_name')\gexec

-- 2. Cerrar las conexiones abiertas, esperando hasta 10 s a que cada una termine
SELECT pg_terminate_backend(pid, 10000)
FROM pg_stat_activity
WHERE datname = :'old_name' AND pid <> pg_backend_pid();

-- 3. Renombrar y volver a permitir conexiones con el nombre nuevo
SELECT format('ALTER DATABASE %I RENAME TO %I', :'old_name', :'new_name')
WHERE EXISTS (SELECT FROM pg_database WHERE datname = :'old_name')\gexec

SELECT format('ALTER DATABASE %I WITH ALLOW_CONNECTIONS true', :'new_name')
WHERE EXISTS (SELECT FROM pg_database WHERE datname = :'new_name')\gexec
