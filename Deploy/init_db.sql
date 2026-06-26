-- Script de inicialización Robusto
-- Ejecutar como superusuario (postgres)

-- 1. Crear Base de Datos (Idempotente)
SELECT 'CREATE DATABASE "AttendanceSystem"'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'AttendanceSystem')\gexec

-- 2. Crear o Actualizar Usuario (Idempotente + Reset Password)
DO
$do$
BEGIN
   IF NOT EXISTS (
      SELECT FROM pg_catalog.pg_roles
      WHERE  rolname = 'attendancesystem_user') THEN
      
      CREATE ROLE attendancesystem_user LOGIN PASSWORD 'Blanquita.123';
   ELSE
      -- Si ya existe, aseguramos que la contraseña sea la correcta
      ALTER ROLE attendancesystem_user WITH PASSWORD 'Blanquita.123';
   END IF;
END
$do$;

-- 3. Asignar privilegios generales
GRANT ALL PRIVILEGES ON DATABASE "AttendanceSystem" TO attendancesystem_user;
ALTER DATABASE "AttendanceSystem" OWNER TO attendancesystem_user;

-- 4. Conectar a la base de datos específica para asignar permisos de esquema
-- NOTA: Esto requiere que psql se ejecute en modo interactivo o scripting que soporte \c
\c "AttendanceSystem";

-- 5. Asegurar permisos en el esquema public DE LA NUEVA BASE DE DATOS
GRANT ALL ON SCHEMA public TO attendancesystem_user;
ALTER SCHEMA public OWNER TO attendancesystem_user;
