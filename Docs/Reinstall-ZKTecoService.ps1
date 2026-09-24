<#
.SYNOPSIS
    Reinstall-ZKTecoService.ps1 - Reinstalación automatizada del servicio Windows ZKTeco.
.DESCRIPTION
    Detiene, elimina, registra nuevamente las librerías COM de ZKTeco, recrea el servicio
    de Windows e inicia el servicio validando su correcto funcionamiento.
.NOTES
    Debe ejecutarse en una consola de PowerShell con permisos de Administrador.
#>

param(
    [string]$ServiceDir = "C:\Program Files\AttendanceSystem\Service",
    [string]$ServiceName = "AttendanceSystem.ZKTeco.Service",
    [string]$DisplayName = "Attendance System ZKTeco Service"
)

# Verificar permisos de Administrador
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Este script debe ser ejecutado como Administrador." -ForegroundColor Red
    Write-Host "Por favor, abra PowerShell como Administrador e intente nuevamente." -ForegroundColor Yellow
    exit 1
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "    REINSTALACIÓN DEL SERVICIO ZKTECO               " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

# Si se ejecuta desde el código fuente, buscar la ruta relativa
if (-not (Test-Path $ServiceDir)) {
    $localRelease = Join-Path $PSScriptRoot "..\Release\Service"
    $localSource = Join-Path $PSScriptRoot "..\src\Presentation\AttendanceSystem.ZKTeco.Service\bin\Release\net10.0\win-x86\publish"
    
    if (Test-Path $localRelease) {
        $ServiceDir = (Resolve-Path $localRelease).Path
    } elseif (Test-Path $localSource) {
        $ServiceDir = (Resolve-Path $localSource).Path
    }
}

$serviceExe = Join-Path $ServiceDir "$ServiceName.exe"

Write-Host "Directorio del Servicio: $ServiceDir" -ForegroundColor Gray
Write-Host "Ejecutable del Servicio: $serviceExe" -ForegroundColor Gray
Write-Host ""

# 1. DETENER SERVICIO EXISTENTE
Write-Host "1. Deteniendo servicio existente (si está en ejecución)..." -ForegroundColor White
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    Write-Host "  - Servicio detenido." -ForegroundColor Green
} else {
    Write-Host "  - El servicio no estaba registrado previamente." -ForegroundColor DarkGray
}

# 2. ELIMINAR REGISTRO PREVIO DEL SERVICIO
Write-Host "2. Eliminando registro del servicio previo en Windows..." -ForegroundColor White
if ($existingService) {
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "  - Servicio eliminado de Windows." -ForegroundColor Green
}

# 3. VERIFICAR ARCHIVOS CRÍTICOS
Write-Host "3. Verificando integridad de archivos..." -ForegroundColor White
if (-not (Test-Path $serviceExe)) {
    Write-Host "  [ERROR] No se encontró el ejecutable: $serviceExe" -ForegroundColor Red
    Write-Host "  Asegúrese de haber compilado la solución o copiado los archivos antes de continuar." -ForegroundColor Yellow
    exit 1
}
Write-Host "  - Ejecutable verificado: $serviceExe" -ForegroundColor Green

# 4. REGISTRAR LIBRERÍAS COM (zkemkeeper.dll)
Write-Host "4. Registrando DLLs COM de ZKTeco en Windows (32-bit)..." -ForegroundColor White
$dllSource = Join-Path $ServiceDir "zkemkeeper.dll"
if (Test-Path $dllSource) {
    $syswow64 = "$env:windir\SysWOW64"
    if (Test-Path $syswow64) {
        Copy-Item -Path "$ServiceDir\*.dll" -Destination $syswow64 -Force -ErrorAction SilentlyContinue
        & regsvr32.exe /s "$syswow64\zkemkeeper.dll"
    } else {
        $sys32 = "$env:windir\System32"
        Copy-Item -Path "$ServiceDir\*.dll" -Destination $sys32 -Force -ErrorAction SilentlyContinue
        & regsvr32.exe /s "$sys32\zkemkeeper.dll"
    }
    Write-Host "  - Registro COM completado exitosamente." -ForegroundColor Green
} else {
    Write-Host "  - [ADVERTENCIA] zkemkeeper.dll no encontrada en el directorio del servicio." -ForegroundColor Yellow
}

# 5. CREAR NUEVO SERVICIO WINDOWS
Write-Host "5. Creando nuevo servicio Windows ($ServiceName)..." -ForegroundColor White
$binPath = """$serviceExe"""
$createResult = & sc.exe create $ServiceName binPath= $binPath start= auto displayname= $DisplayName
& sc.exe description $ServiceName "Servicio puente gRPC (x86) para comunicacion con terminales biometricas ZKTeco" | Out-Null

if ($LASTEXITCODE -eq 0 -or $createResult -match "SUCCESS") {
    Write-Host "  - Servicio registrado exitosamente." -ForegroundColor Green
} else {
    Write-Host "  [ERROR] Falló la creación del servicio: $createResult" -ForegroundColor Red
    exit 1
}

# 6. INICIAR EL SERVICIO
Write-Host "6. Iniciando el servicio..." -ForegroundColor White
Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

$status = (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status
if ($status -eq "Running") {
    Write-Host "  - Servicio iniciado y en estado: Running" -ForegroundColor Green
} else {
    Write-Host "  [ALERTA] El servicio tiene estado: $status. Revise el Visor de Eventos." -ForegroundColor Yellow
}

# 7. VALIDAR PUERTO GRPC 5001
Write-Host "7. Comprobando escucha en puerto gRPC (5001)..." -ForegroundColor White
$portCheck = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
if ($portCheck) {
    Write-Host "  - Puerto 5001 escuchando correctamente." -ForegroundColor Green
} else {
    Write-Host "  [ALERTA] El puerto 5001 aún no responde. Puede tomar unos segundos adicionales." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "✅ Proceso de reinstalación finalizado." -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Cyan
