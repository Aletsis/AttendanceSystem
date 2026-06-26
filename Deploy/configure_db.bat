@echo off
setlocal
set PGPASSWORD=%~1
set PGUSER=postgres
set PGBIN=%~2

if "%PGBIN%"=="" set PGBIN=psql

echo Configurando Base de Datos...

REM 1. Crear Usuario (Ignorar error si existe)
"%PGBIN%" -U %PGUSER% -d postgres -c "CREATE ROLE attendancesystem_user LOGIN PASSWORD 'Blanquita.123';" 2>nul
REM Asegurar contraseña correcta
"%PGBIN%" -U %PGUSER% -d postgres -c "ALTER ROLE attendancesystem_user WITH PASSWORD 'Blanquita.123';"

REM 2. Crear Base de Datos
"%PGBIN%" -U %PGUSER% -d postgres -c "CREATE DATABASE \"AttendanceSystem\" OWNER attendancesystem_user;" 2>nul
REM Si la base ya existía y era de postgres, cambiar el dueño
"%PGBIN%" -U %PGUSER% -d postgres -c "ALTER DATABASE \"AttendanceSystem\" OWNER TO attendancesystem_user;"

REM 3. Configurar Privilegios (En la nueva DB)
REM Dar permisos sobre el esquema public
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL ON SCHEMA public TO attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "ALTER SCHEMA public OWNER TO attendancesystem_user;"

REM 4. Dar permisos a TODAS las tablas y secuencias existentes
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO attendancesystem_user;"

REM 5. Permisos para Hangfire (Crear esquemas y manejar su esquema propio)
REM Permitir crear nuevos esquemas en la base de datos
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT CREATE ON DATABASE \"AttendanceSystem\" TO attendancesystem_user;"
REM Si el esquema hangfire ya existe, dar permiso total
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL ON SCHEMA hangfire TO attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO attendancesystem_user;"
"%PGBIN%" -U %PGUSER% -d "AttendanceSystem" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO attendancesystem_user;"

echo Configuracion de BD Terminada.
exit /b 0
