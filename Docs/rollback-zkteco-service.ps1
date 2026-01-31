# Script de Rollback para el Servicio ZKTeco
# Ejecutar como Administrador

param(
    [string]$BackupPath = "",
    [string]$ServicePath = "C:\Services\AttendanceSystem.ZKTeco",
    [string]$ServiceName = "AttendanceSystem.ZKTeco"
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

Write-Warning "=== Rollback de AttendanceSystem.ZKTeco.Service ==="
Write-Warning "Este script revertirá el servicio a una versión anterior"

# Si no se especificó BackupPath, mostrar backups disponibles
if ($BackupPath -eq "") {
    Write-Info "`nBackups disponibles:"
    
    $backupsRoot = "C:\Backups\ZKTecoService"
    
    if (Test-Path $backupsRoot) {
        $backups = Get-ChildItem -Path $backupsRoot -Directory | Sort-Object Name -Descending
        
        if ($backups.Count -eq 0) {
            Write-Error "No se encontraron backups en $backupsRoot"
            exit 1
        }
        
        for ($i = 0; $i -lt $backups.Count; $i++) {
            $backup = $backups[$i]
            $size = (Get-ChildItem -Path $backup.FullName -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
            Write-Host "  [$i] $($backup.Name) - $([math]::Round($size, 2)) MB - $($backup.LastWriteTime)"
        }
        
        $selection = Read-Host "`nSelecciona el número del backup a restaurar"
        
        if ($selection -match '^\d+$' -and [int]$selection -lt $backups.Count) {
            $BackupPath = $backups[[int]$selection].FullName
        }
        else {
            Write-Error "Selección inválida"
            exit 1
        }
    }
    else {
        Write-Error "No se encontró la carpeta de backups: $backupsRoot"
        exit 1
    }
}

# Validar que existe el backup
if (-not (Test-Path $BackupPath)) {
    Write-Error "El backup especificado no existe: $BackupPath"
    exit 1
}

Write-Warning "`nSe restaurará desde: $BackupPath"
Write-Warning "Destino: $ServicePath"

$confirm = Read-Host "`n¿Estás seguro de continuar con el rollback? (s/n)"
if ($confirm -ne "s") {
    Write-Info "Rollback cancelado"
    exit 0
}

# 1. Crear backup de seguridad de la versión actual
Write-Info "`n[1/4] Creando backup de seguridad de la versión actual..."
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safetyBackupPath = "C:\Backups\ZKTecoService\before_rollback_$timestamp"

New-Item -Path $safetyBackupPath -ItemType Directory -Force | Out-Null
Copy-Item -Path "$ServicePath\*" -Destination $safetyBackupPath -Recurse -Force

Write-Success "Backup de seguridad creado en: $safetyBackupPath"

# 2. Detener el servicio
Write-Info "`n[2/4] Deteniendo servicio..."

try {
    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    Write-Success "Servicio detenido"
}
catch {
    Write-Warning "No se pudo detener el servicio: $($_.Exception.Message)"
}

Write-Info "Esperando a que el proceso se detenga..."
Start-Sleep -Seconds 5

# Verificar que no hay procesos bloqueando archivos
$processes = Get-Process | Where-Object { $_.Path -like "$ServicePath\*" }
if ($processes) {
    Write-Warning "Hay procesos usando archivos del servicio. Intentando detenerlos..."
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. Restaurar desde backup
Write-Info "`n[3/4] Restaurando desde backup..."

try {
    # Preservar appsettings.json actual
    $appsettingsPath = Join-Path $ServicePath "appsettings.json"
    $tempSettings = "$env:TEMP\zkteco_appsettings.json.bak"
    
    if (Test-Path $appsettingsPath) {
        Write-Info "Preservando appsettings.json actual..."
        Copy-Item -Path $appsettingsPath -Destination $tempSettings -Force
    }
    
    # Limpiar carpeta actual (excepto logs)
    Get-ChildItem -Path $ServicePath -Exclude "logs" | Remove-Item -Recurse -Force
    
    # Copiar desde backup
    Copy-Item -Path "$BackupPath\*" -Destination $ServicePath -Recurse -Force
    
    # Preguntar si restaurar configuración
    Write-Info "`n¿Qué configuración deseas usar?"
    Write-Info "  [1] Configuración del backup (antigua)"
    Write-Info "  [2] Configuración actual (preservar cambios)"
    $configChoice = Read-Host "Selección (1 o 2)"
    
    if ($configChoice -eq "2" -and (Test-Path $tempSettings)) {
        Copy-Item -Path $tempSettings -Destination $appsettingsPath -Force
        Write-Success "Configuración actual preservada"
    }
    else {
        Write-Success "Usando configuración del backup"
    }
    
    # Limpiar temporal
    if (Test-Path $tempSettings) {
        Remove-Item -Path $tempSettings -Force
    }
    
    Write-Success "Archivos restaurados correctamente"
}
catch {
    Write-Error "Error al restaurar archivos: $($_.Exception.Message)"
    Write-Error "El servicio puede estar en un estado inconsistente"
    Write-Info "Puedes intentar restaurar manualmente desde: $safetyBackupPath"
    exit 1
}

# 4. Reiniciar el servicio
Write-Info "`n[4/4] Reiniciando servicio..."

try {
    Start-Service -Name $ServiceName -ErrorAction Stop
    Write-Success "Servicio iniciado"
}
catch {
    Write-Error "Error al iniciar servicio: $($_.Exception.Message)"
}

Start-Sleep -Seconds 3

# Verificar estado
$service = Get-Service -Name $ServiceName

Write-Info "`n=== Estado del Rollback ==="
Write-Info "Servicio: $($service.Status)"

if ($service.Status -eq "Running") {
    Write-Success "`n✓ Rollback completado exitosamente"
    
    # Verificar puerto gRPC
    Start-Sleep -Seconds 2
    
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
            }
        }
    }
}
else {
    Write-Error "`n✗ Hay problemas después del rollback"
    Write-Info "Revisa los logs en: $ServicePath\logs"
}

Write-Info "`n=== Información Adicional ==="
Write-Info "Versión restaurada desde: $BackupPath"
Write-Info "Backup de seguridad en: $safetyBackupPath"
Write-Info "`nSi el rollback fue exitoso, puedes eliminar el backup de seguridad:"
Write-Info "  Remove-Item -Path '$safetyBackupPath' -Recurse -Force"

Write-Info "`nPara ver los logs:"
Write-Info "  Get-Content '$ServicePath\logs\*.log' -Tail 50 -Wait"
