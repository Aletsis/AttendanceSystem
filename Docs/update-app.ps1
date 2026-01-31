# Script de Actualización de AttendanceSystem
# Ejecutar como Administrador

param(
    [string]$PublishPath = "C:\Publish\AttendanceSystem",
    [string]$SiteName = "AttendanceSystem",
    [string]$AppPoolName = "AttendanceSystemPool",
    [string]$NewVersionPath = "",
    [switch]$SkipBackup,
    [switch]$ApplyMigrations
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

Write-Info "=== Actualización de AttendanceSystem ==="

# Validar que existe la nueva versión
if ($NewVersionPath -eq "" -or -not (Test-Path $NewVersionPath)) {
    Write-Error "Debe especificar una ruta válida con -NewVersionPath"
    Write-Info "Ejemplo: .\update-app.ps1 -NewVersionPath 'C:\Temp\NewPublish'"
    exit 1
}

# 1. Crear backup
if (-not $SkipBackup) {
    Write-Info "`n[1/6] Creando backup..."
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupPath = "C:\Backups\AttendanceSystem\$timestamp"
    
    New-Item -Path $backupPath -ItemType Directory -Force | Out-Null
    Copy-Item -Path "$PublishPath\*" -Destination $backupPath -Recurse -Force
    
    Write-Success "Backup creado en: $backupPath"
}
else {
    Write-Warning "Omitiendo backup (SkipBackup activado)"
}

# 2. Detener el sitio
Write-Info "`n[2/6] Deteniendo sitio y Application Pool..."

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

# Esperar a que se detenga completamente
Write-Info "Esperando a que los procesos se detengan..."
Start-Sleep -Seconds 5

# Verificar que no hay procesos bloqueando archivos
$processes = Get-Process | Where-Object { $_.Path -like "$PublishPath\*" }
if ($processes) {
    Write-Warning "Hay procesos usando archivos de la aplicación. Intentando detenerlos..."
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. Copiar nueva versión
Write-Info "`n[3/6] Copiando nueva versión..."

try {
    # Preservar appsettings.Production.json
    $productionSettings = Join-Path $PublishPath "appsettings.Production.json"
    $tempSettings = "$env:TEMP\appsettings.Production.json.bak"
    
    if (Test-Path $productionSettings) {
        Write-Info "Preservando appsettings.Production.json..."
        Copy-Item -Path $productionSettings -Destination $tempSettings -Force
    }
    
    # Copiar nueva versión
    Copy-Item -Path "$NewVersionPath\*" -Destination $PublishPath -Recurse -Force
    
    # Restaurar appsettings.Production.json
    if (Test-Path $tempSettings) {
        Copy-Item -Path $tempSettings -Destination $productionSettings -Force
        Remove-Item -Path $tempSettings -Force
        Write-Success "appsettings.Production.json restaurado"
    }
    
    Write-Success "Nueva versión copiada correctamente"
}
catch {
    Write-Error "Error al copiar archivos: $($_.Exception.Message)"
    Write-Error "Considera restaurar desde el backup"
    exit 1
}

# 4. Aplicar migraciones (opcional)
if ($ApplyMigrations) {
    Write-Info "`n[4/6] Aplicando migraciones de base de datos..."
    
    $dllPath = Join-Path $PublishPath "AttendanceSystem.Blazor.Server.dll"
    
    if (Test-Path $dllPath) {
        try {
            # Establecer variable de entorno
            $env:ASPNETCORE_ENVIRONMENT = "Production"
            
            # Ejecutar migraciones (esto depende de cómo tengas configurado tu app)
            # Opción 1: Si tienes un comando personalizado
            # dotnet $dllPath --migrate
            
            # Opción 2: Usando EF Core CLI (requiere que esté instalado)
            # cd $PublishPath
            # dotnet ef database update
            
            Write-Warning "Las migraciones deben aplicarse manualmente o configurar el comando apropiado"
            Write-Info "Presiona Enter para continuar..."
            Read-Host
            
        }
        catch {
            Write-Error "Error al aplicar migraciones: $($_.Exception.Message)"
            $continue = Read-Host "¿Continuar de todas formas? (s/n)"
            if ($continue -ne "s") { exit 1 }
        }
    }
}
else {
    Write-Info "`n[4/6] Omitiendo migraciones (ApplyMigrations no activado)"
}

# 5. Limpiar caché y archivos temporales
Write-Info "`n[5/6] Limpiando caché..."

# Limpiar logs antiguos (mantener últimos 7 días)
$logsPath = Join-Path $PublishPath "logs"
if (Test-Path $logsPath) {
    $cutoffDate = (Get-Date).AddDays(-7)
    Get-ChildItem -Path $logsPath -File | Where-Object { $_.LastWriteTime -lt $cutoffDate } | Remove-Item -Force
    Write-Success "Logs antiguos eliminados"
}

# 6. Reiniciar el sitio
Write-Info "`n[6/6] Reiniciando sitio..."

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

Write-Info "`n=== Estado del Despliegue ==="
Write-Info "Sitio: $($site.State)"
Write-Info "Application Pool: $($pool.Value)"

if ($site.State -eq "Started" -and $pool.Value -eq "Started") {
    Write-Success "`n✓ Actualización completada exitosamente"
}
else {
    Write-Error "`n✗ Hay problemas con el despliegue"
    Write-Info "Revisa los logs en: $logsPath"
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

Write-Info "`n=== Información de Rollback ==="
if (-not $SkipBackup) {
    Write-Info "Si necesitas revertir los cambios, ejecuta:"
    Write-Info "  .\rollback-app.ps1 -BackupPath '$backupPath'"
}

Write-Info "`nPara ver los logs:"
Write-Info "  - Application logs: $logsPath"
Write-Info "  - Event Viewer: eventvwr.msc → Windows Logs → Application"
