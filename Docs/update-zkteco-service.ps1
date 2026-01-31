# Script de Actualización del Servicio ZKTeco
# Ejecutar como Administrador

param(
    [string]$ServicePath = "C:\Services\AttendanceSystem.ZKTeco",
    [string]$ServiceName = "AttendanceSystem.ZKTeco",
    [string]$NewVersionPath = "",
    [switch]$SkipBackup
)

function Write-Success { param($msg) Write-Host $msg -ForegroundColor Green }
function Write-Info { param($msg) Write-Host $msg -ForegroundColor Cyan }
function Write-Warning { param($msg) Write-Host $msg -ForegroundColor Yellow }
function Write-Error { param($msg) Write-Host $msg -ForegroundColor Red }

# Verificar que se ejecuta como administrador
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script debe ejecutarse como Administrador"
    exit 1
}

Write-Info "=== Actualización de AttendanceSystem.ZKTeco.Service ==="

# Validar que existe la nueva versión
if ($NewVersionPath -eq "" -or -not (Test-Path $NewVersionPath)) {
    Write-Error "Debe especificar una ruta válida con -NewVersionPath"
    Write-Info "Ejemplo: .\update-zkteco-service.ps1 -NewVersionPath 'C:\Temp\NewServicePublish'"
    exit 1
}

# Validar que existe el servicio
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Error "El servicio '$ServiceName' no existe"
    Write-Info "Usa install-zkteco-service.ps1 para instalarlo primero"
    exit 1
}

# 1. Crear backup
if (-not $SkipBackup) {
    Write-Info "`n[1/5] Creando backup..."
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupPath = "C:\Backups\ZKTecoService\$timestamp"
    
    New-Item -Path $backupPath -ItemType Directory -Force | Out-Null
    Copy-Item -Path "$ServicePath\*" -Destination $backupPath -Recurse -Force
    
    Write-Success "Backup creado en: $backupPath"
}
else {
    Write-Warning "Omitiendo backup (SkipBackup activado)"
}

# 2. Detener el servicio
Write-Info "`n[2/5] Deteniendo servicio..."

try {
    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    Write-Success "Servicio detenido"
}
catch {
    Write-Error "Error al detener el servicio: $($_.Exception.Message)"
    exit 1
}

# Esperar a que se detenga completamente
Write-Info "Esperando a que el proceso se detenga..."
Start-Sleep -Seconds 5

# Verificar que no hay procesos bloqueando archivos
$processes = Get-Process | Where-Object { $_.Path -like "$ServicePath\*" }
if ($processes) {
    Write-Warning "Hay procesos usando archivos del servicio. Intentando detenerlos..."
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. Copiar nueva versión
Write-Info "`n[3/5] Copiando nueva versión..."

try {
    # Preservar appsettings.json
    $appsettingsPath = Join-Path $ServicePath "appsettings.json"
    $tempSettings = "$env:TEMP\zkteco_appsettings.json.bak"
    
    if (Test-Path $appsettingsPath) {
        Write-Info "Preservando appsettings.json..."
        Copy-Item -Path $appsettingsPath -Destination $tempSettings -Force
    }
    
    # Copiar nueva versión
    Copy-Item -Path "$NewVersionPath\*" -Destination $ServicePath -Recurse -Force
    
    # Restaurar appsettings.json
    if (Test-Path $tempSettings) {
        Copy-Item -Path $tempSettings -Destination $appsettingsPath -Force
        Remove-Item -Path $tempSettings -Force
        Write-Success "appsettings.json restaurado"
    }
    
    Write-Success "Nueva versión copiada correctamente"
}
catch {
    Write-Error "Error al copiar archivos: $($_.Exception.Message)"
    Write-Error "Considera restaurar desde el backup"
    exit 1
}

# 4. Limpiar logs antiguos
Write-Info "`n[4/5] Limpiando logs antiguos..."

$logsPath = Join-Path $ServicePath "logs"
if (Test-Path $logsPath) {
    $cutoffDate = (Get-Date).AddDays(-7)
    $deletedCount = 0
    
    Get-ChildItem -Path $logsPath -File | Where-Object { $_.LastWriteTime -lt $cutoffDate } | ForEach-Object {
        Remove-Item $_.FullName -Force
        $deletedCount++
    }
    
    Write-Success "Logs antiguos eliminados: $deletedCount archivos"
}

# 5. Reiniciar el servicio
Write-Info "`n[5/5] Reiniciando servicio..."

try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Write-Success "Servicio iniciado"
}
catch {
    Write-Error "Error al iniciar servicio: $($_.Exception.Message)"
    Write-Info "Revisa los logs en: $logsPath"
}

Start-Sleep -Seconds 3

# Verificar estado
$service = Get-Service -Name $ServiceName

Write-Info "`n=== Estado de la Actualización ==="
Write-Info "Servicio: $($service.Status)"

if ($service.Status -eq "Running") {
    Write-Success "`n✓ Actualización completada exitosamente"
    
    # Verificar puerto gRPC
    Start-Sleep -Seconds 2
    
    # Leer puerto de configuración
    $appsettingsPath = Join-Path $ServicePath "appsettings.json"
    if (Test-Path $appsettingsPath) {
        $config = Get-Content $appsettingsPath | ConvertFrom-Json
        $grpcPort = $config.GrpcPort
        
        if ($grpcPort) {
            $port = Get-NetTCPConnection -LocalPort $grpcPort -State Listen -ErrorAction SilentlyContinue
            
            if ($null -ne $port) {
                Write-Success "✓ Puerto gRPC $grpcPort está escuchando"
            }
            else {
                Write-Warning "⚠ Puerto gRPC $grpcPort no está escuchando aún"
                Write-Info "Espera unos segundos y verifica con: Get-NetTCPConnection -LocalPort $grpcPort"
            }
        }
    }
}
else {
    Write-Error "`n✗ Hay problemas con la actualización"
    Write-Info "Revisa los logs en: $logsPath"
}

Write-Info "`n=== Información de Rollback ==="
if (-not $SkipBackup) {
    Write-Info "Si necesitas revertir los cambios, ejecuta:"
    Write-Info "  .\rollback-zkteco-service.ps1 -BackupPath '$backupPath'"
}

Write-Info "`nPara ver los logs:"
Write-Info "  Get-Content '$logsPath\*.log' -Tail 50 -Wait"

Write-Info "`nPara verificar el servicio:"
Write-Info "  Get-Service -Name '$ServiceName'"
Write-Info "  Get-NetTCPConnection -LocalPort 5001"
