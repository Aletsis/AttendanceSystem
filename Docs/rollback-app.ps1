# Script de Rollback para AttendanceSystem
# Ejecutar como Administrador

param(
    [string]$BackupPath = "",
    [string]$PublishPath = "C:\Publish\AttendanceSystem",
    [string]$SiteName = "AttendanceSystem",
    [string]$AppPoolName = "AttendanceSystemPool"
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

Write-Warning "=== Rollback de AttendanceSystem ==="
Write-Warning "Este script revertirá la aplicación a una versión anterior"

# Si no se especificó BackupPath, mostrar backups disponibles
if ($BackupPath -eq "") {
    Write-Info "`nBackups disponibles:"
    
    $backupsRoot = "C:\Backups\AttendanceSystem"
    
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
Write-Warning "Destino: $PublishPath"

$confirm = Read-Host "`n¿Estás seguro de continuar con el rollback? (s/n)"
if ($confirm -ne "s") {
    Write-Info "Rollback cancelado"
    exit 0
}

# 1. Crear backup de la versión actual (por si acaso)
Write-Info "`n[1/4] Creando backup de seguridad de la versión actual..."
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safetyBackupPath = "C:\Backups\AttendanceSystem\before_rollback_$timestamp"

New-Item -Path $safetyBackupPath -ItemType Directory -Force | Out-Null
Copy-Item -Path "$PublishPath\*" -Destination $safetyBackupPath -Recurse -Force

Write-Success "Backup de seguridad creado en: $safetyBackupPath"

# 2. Detener el sitio
Write-Info "`n[2/4] Deteniendo sitio y Application Pool..."

Import-Module WebAdministration

try {
    Stop-Website -Name $SiteName -ErrorAction Stop
    Write-Success "Sitio detenido"
}
catch {
    Write-Warning "No se pudo detener el sitio: $($_.Exception.Message)"
}

try {
    Stop-WebAppPool -Name $AppPoolName -ErrorAction Stop
    Write-Success "Application Pool detenido"
}
catch {
    Write-Warning "No se pudo detener el Application Pool: $($_.Exception.Message)"
}

Write-Info "Esperando a que los procesos se detengan..."
Start-Sleep -Seconds 5

# Verificar que no hay procesos bloqueando archivos
$processes = Get-Process | Where-Object { $_.Path -like "$PublishPath\*" }
if ($processes) {
    Write-Warning "Hay procesos usando archivos de la aplicación. Intentando detenerlos..."
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. Restaurar desde backup
Write-Info "`n[3/4] Restaurando desde backup..."

try {
    # Limpiar carpeta actual (excepto appsettings.Production.json y logs)
    $productionSettings = Join-Path $PublishPath "appsettings.Production.json"
    $tempSettings = "$env:TEMP\appsettings.Production.json.bak"
    
    if (Test-Path $productionSettings) {
        Write-Info "Preservando appsettings.Production.json..."
        Copy-Item -Path $productionSettings -Destination $tempSettings -Force
    }
    
    # Eliminar archivos actuales
    Get-ChildItem -Path $PublishPath -Exclude "logs", "appsettings.Production.json" | Remove-Item -Recurse -Force
    
    # Copiar desde backup
    Copy-Item -Path "$BackupPath\*" -Destination $PublishPath -Recurse -Force
    
    # Restaurar appsettings.Production.json
    if (Test-Path $tempSettings) {
        Copy-Item -Path $tempSettings -Destination $productionSettings -Force
        Remove-Item -Path $tempSettings -Force
        Write-Success "appsettings.Production.json restaurado"
    }
    
    Write-Success "Archivos restaurados correctamente"
}
catch {
    Write-Error "Error al restaurar archivos: $($_.Exception.Message)"
    Write-Error "La aplicación puede estar en un estado inconsistente"
    Write-Info "Puedes intentar restaurar manualmente desde: $safetyBackupPath"
    exit 1
}

# 4. Reiniciar el sitio
Write-Info "`n[4/4] Reiniciando sitio..."

try {
    Start-WebAppPool -Name $AppPoolName -ErrorAction Stop
    Write-Success "Application Pool iniciado"
}
catch {
    Write-Error "Error al iniciar Application Pool: $($_.Exception.Message)"
}

Start-Sleep -Seconds 2

try {
    Start-Website -Name $SiteName -ErrorAction Stop
    Write-Success "Sitio iniciado"
}
catch {
    Write-Error "Error al iniciar sitio: $($_.Exception.Message)"
}

Start-Sleep -Seconds 3

# Verificar estado
$site = Get-Website -Name $SiteName
$pool = Get-WebAppPoolState -Name $AppPoolName

Write-Info "`n=== Estado del Rollback ==="
Write-Info "Sitio: $($site.State)"
Write-Info "Application Pool: $($pool.Value)"

if ($site.State -eq "Started" -and $pool.Value -eq "Started") {
    Write-Success "`n✓ Rollback completado exitosamente"
}
else {
    Write-Error "`n✗ Hay problemas después del rollback"
    Write-Info "Revisa los logs en: $PublishPath\logs"
}

# Probar el sitio
Write-Info "`nProbando el sitio..."
Start-Sleep -Seconds 5

try {
    $binding = $site.bindings.Collection[0]
    $port = $binding.bindingInformation.Split(':')[1]
    $url = "http://localhost:$port"
    
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
    
    if ($response.StatusCode -eq 200) {
        Write-Success "✓ El sitio responde correctamente (HTTP 200)"
    }
    else {
        Write-Warning "El sitio respondió con código: $($response.StatusCode)"
    }
}
catch {
    Write-Warning "No se pudo probar el sitio: $($_.Exception.Message)"
    Write-Info "Verifica manualmente accediendo a la URL del sitio"
}

Write-Info "`n=== Información Adicional ==="
Write-Info "Versión restaurada desde: $BackupPath"
Write-Info "Backup de seguridad en: $safetyBackupPath"
Write-Info "`nSi el rollback fue exitoso, puedes eliminar el backup de seguridad:"
Write-Info "  Remove-Item -Path '$safetyBackupPath' -Recurse -Force"
