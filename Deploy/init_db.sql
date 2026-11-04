-- Configuración de la base de datos de AttendanceSystem (idempotente).
-- Ejecutar como superusuario (postgres) con psql, pasando la contraseña del usuario de la aplicación:
--   psql -w -v ON_ERROR_STOP=1 -U postgres -d postgres -v app_password=<contraseña> -f init_db.sql
-- El instalador lo ejecuta sólo en la primera instalación (cuando aún no existe appsettings.Production.json)
-- y escribe la misma contraseña en la cadena de conexión de la aplicación.
-- Sin -v app_password el ALTER ROLE falla y, con ON_ERROR_STOP, psql termina con error.

-- 1. Usuario de la aplicación: se crea si no existe y su contraseña se fija a la que usará la aplicación.
SELECT 'CREATE ROLE attendancesystem_user LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'attendancesystem_user')\gexec

ALTER ROLE attendancesystem_user WITH LOGIN PASSWORD :'app_password';

-- 2. Base de datos, propiedad del usuario de la aplicación.
SELECT 'CREATE DATABASE "AttendanceSystem" OWNER attendancesystem_user'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'AttendanceSystem')\gexec

ALTER DATABASE "AttendanceSystem" OWNER TO attendancesystem_user;
GRANT ALL PRIVILEGES ON DATABASE "AttendanceSystem" TO attendancesystem_user;

\connect "AttendanceSystem"

-- 3. Esquema public: la restauración de respaldos lo borra y lo recrea, así que el usuario debe ser su dueño.
ALTER SCHEMA public OWNER TO attendancesystem_user;
GRANT ALL ON SCHEMA public TO attendancesystem_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO attendancesystem_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO attendancesystem_user;
GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO attendancesystem_user;

-- 4. Esquema de Hangfire.
CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION attendancesystem_user;
ALTER SCHEMA hangfire OWNER TO attendancesystem_user;
GRANT ALL ON SCHEMA hangfire TO attendancesystem_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO attendancesystem_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO attendancesystem_user;
