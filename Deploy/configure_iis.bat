@echo off
setlocal EnableDelayedExpansion

REM Usar el directorio temporal para el log para evitar problemas de permisos
set LOGFILE=%TEMP%\iis_install_log.txt

echo DO NOT CLOSE THIS WINDOW > "%LOGFILE%"
echo ==================================== >> "%LOGFILE%"
echo Configurando AttendanceSystem en IIS >> "%LOGFILE%"
echo Fecha: %DATE% %TIME% >> "%LOGFILE%"
echo ==================================== >> "%LOGFILE%"

REM Intentar encontrar AppCmd en varias ubicaciones debido a redirección de 32/64 bits
set APPCMD=%windir%\system32\inetsrv\appcmd.exe

if not exist "%APPCMD%" (
    REM Intentar buscar en Sysnative (para acceder a System32 de 64 bits desde proceso de 32 bits)
    set APPCMD=%windir%\Sysnative\inetsrv\appcmd.exe
)

if not exist "%APPCMD%" (
    echo [ERROR] No se encuentra appcmd.exe en ninguna ruta. >> "%LOGFILE%"
    echo [INFO] Ruta probada 1: %windir%\system32\inetsrv\appcmd.exe >> "%LOGFILE%"
    echo [INFO] Ruta probada 2: %windir%\Sysnative\inetsrv\appcmd.exe >> "%LOGFILE%"
    echo [INFO] ¿Esta habilitado el rol de IIS? >> "%LOGFILE%"
    exit /b 1
)

echo [INFO] Usando AppCmd: "!APPCMD!" >> "%LOGFILE%"

set "WEB_DIR=%~1"
echo [INFO] Directorio Web: "!WEB_DIR!" >> "%LOGFILE%"

echo 1. Gestionando AppPool... >> "%LOGFILE%"
"!APPCMD!" list apppool "AttendanceSystem" >> "%LOGFILE%" 2>&1
if !ERRORLEVEL! NEQ 0 (
    echo    - Creando AppPool 'AttendanceSystem'... >> "%LOGFILE%"
    "!APPCMD!" add apppool /name:AttendanceSystem /managedRuntimeVersion:"" /managedPipelineMode:Integrated >> "%LOGFILE%" 2>&1
    if !ERRORLEVEL! NEQ 0 (
        echo [ERROR] Fallo al crear AppPool. >> "%LOGFILE%"
        exit /b 1
    )
) else (
    echo    - El AppPool ya existe. >> "%LOGFILE%"
)

echo 2. Configurando Sitio Web... >> "%LOGFILE%"
"!APPCMD!" list site "AttendanceSystem" >> "%LOGFILE%" 2>&1
if !ERRORLEVEL! EQU 0 (
    echo    - Eliminando sitio existente para reconfigurar... >> "%LOGFILE%"
    "!APPCMD!" delete site "AttendanceSystem" >> "%LOGFILE%" 2>&1
)

echo    - Intentando detener 'Default Web Site'... >> "%LOGFILE%"
"!APPCMD!" stop site "Default Web Site" >> "%LOGFILE%" 2>&1

timeout /t 2 /nobreak >nul

echo    - Creando Sitio Web 'AttendanceSystem' en puerto 80... >> "%LOGFILE%"
"!APPCMD!" add site /name:AttendanceSystem /bindings:http/*:80: /physicalPath:"!WEB_DIR!" >> "%LOGFILE%" 2>&1

if !ERRORLEVEL! NEQ 0 (
    echo [ADVERTENCIA] Puerto 80 ocupado. Probando puerto 8081... >> "%LOGFILE%"
    "!APPCMD!" add site /name:AttendanceSystem /bindings:http/*:8081: /physicalPath:"!WEB_DIR!" >> "%LOGFILE%" 2>&1
    if !ERRORLEVEL! NEQ 0 (
        echo [ERROR] Fallo al crear sitio en puertos 80 y 8081. >> "%LOGFILE%"
        exit /b 1
    ) else (
        echo [EXITO] Sitio creado en puerto 8081. >> "%LOGFILE%"
    )
)

echo 3. Asignando AppPool... >> "%LOGFILE%"
"!APPCMD!" set site "AttendanceSystem" -applicationPool:AttendanceSystem >> "%LOGFILE%" 2>&1

echo 4. Configurando permisos de Escritura/Modificación... >> "%LOGFILE%"
REM Otorgamos permiso de Modificar (M) para que la App pueda escribir Logs y Backups
icacls "!WEB_DIR!" /grant "IIS_IUSRS":(OI)(CI)M /T /Q >> "%LOGFILE%" 2>&1

REM Si existe una carpeta de Backups o Logs en el nivel superior, intentar dar permisos también
REM Asumimos que WEB_DIR es ...\AttendanceSystem\Web, así que subimos un nivel
for %%I in ("!WEB_DIR!\..") do set "ROOT_DIR=%%~fI"
echo [INFO] Directorio Raiz detectado: "!ROOT_DIR!" >> "%LOGFILE%"

if exist "!ROOT_DIR!\Backups" (
   echo    - Dando permisos a carpeta Backups... >> "%LOGFILE%"
   icacls "!ROOT_DIR!\Backups" /grant "IIS_IUSRS":(OI)(CI)M /T /Q >> "%LOGFILE%" 2>&1
)
if exist "!ROOT_DIR!\Logs" (
   echo    - Dando permisos a carpeta Logs... >> "%LOGFILE%"
   icacls "!ROOT_DIR!\Logs" /grant "IIS_IUSRS":(OI)(CI)M /T /Q >> "%LOGFILE%" 2>&1
)

echo 5. Iniciando Sitio... >> "%LOGFILE%"
"!APPCMD!" start site "AttendanceSystem" >> "%LOGFILE%" 2>&1

echo ==================================== >> "%LOGFILE%"
echo EXITOSO >> "%LOGFILE%"
exit /b 0
