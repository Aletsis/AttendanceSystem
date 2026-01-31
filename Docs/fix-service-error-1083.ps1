# Script para Solucionar Error 1083 del Servicio ZKTeco
# Ejecutar como Administrador

param(
    [string]$ServicePath = "C:\Services\AttendanceSystem.ZKTeco",
    [string]$ServiceName = "AttendanceSystem.ZKTeco",
    [switch]$UseNSSM,
    [string]$NSSMPath = "C:\Tools\nssm\win64\nssm.exe"
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

Write-Info "=== Solucionando Error 1083 del Servicio ZKTeco ==="

# 1. Detener y eliminar servicio existente
Write-Info "`n[1/4] Eliminando servicio existente..."

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    Write-Info "Deteniendo servicio..."
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    
    Write-Info "Eliminando servicio..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    
    Write-Success "Servicio eliminado"
}
else {
    Write-Info "No hay servicio existente"
}

# 2. Re-publicar el servicio
Write-Info "`n[2/4] Re-publicando el servicio..."

# Buscar el proyecto
$projectPath = Get-ChildItem -Path "." -Filter "AttendanceSystem.ZKTeco.Service.csproj" -Recurse | Select-Object -First 1

if ($null -eq $projectPath) {
    Write-Error "No se encontró el proyecto AttendanceSystem.ZKTeco.Service.csproj"
    Write-Info "Asegúrate de ejecutar este script desde la carpeta del repositorio"
    exit 1
}

Write-Info "Proyecto encontrado: $($projectPath.FullName)"

# Crear backup si existe versión anterior
if (Test-Path $ServicePath) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupPath = "C:\Backups\ZKTecoService_$timestamp"
    
    Write-Info "Creando backup en: $backupPath"
    New-Item -Path "C:\Backups" -ItemType Directory -Force | Out-Null
    Copy-Item -Path $ServicePath -Destination $backupPath -Recurse -Force
    
    # Preservar appsettings.json
    $appsettingsPath = Join-Path $ServicePath "appsettings.json"
    $tempSettings = "$env:TEMP\zkteco_appsettings.json.bak"
    
    if (Test-Path $appsettingsPath) {
        Copy-Item -Path $appsettingsPath -Destination $tempSettings -Force
    }
}

# Publicar
Write-Info "Publicando servicio (win-x86)..."
dotnet publish $projectPath.FullName -c Release -o $ServicePath --runtime win-x86 --self-contained true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error al publicar el servicio"
    exit 1
}

# Restaurar appsettings.json
if (Test-Path $tempSettings) {
    Copy-Item -Path $tempSettings -Destination $appsettingsPath -Force
    Remove-Item -Path $tempSettings -Force
    Write-Success "Configuración restaurada"
}

Write-Success "Servicio publicado correctamente"

# 3. Crear carpeta de logs
Write-Info "`n[3/4] Configurando logs..."
$logsPath = Join-Path $ServicePath "logs"
New-Item -Path $logsPath -ItemType Directory -Force | Out-Null

# 4. Reinstalar el servicio
Write-Info "`n[4/4] Reinstalando servicio..."

$exePath = Join-Path $ServicePath "AttendanceSystem.ZKTeco.Service.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "No se encontró el ejecutable: $exePath"
    exit 1
}

if ($UseNSSM -and (Test-Path $NSSMPath)) {
    Write-Info "Instalando con NSSM..."
    
    & $NSSMPath install $ServiceName $exePath
    & $NSSMPath set $ServiceName AppDirectory $ServicePath
    & $NSSMPath set $ServiceName DisplayName "AttendanceSystem ZKTeco Service"
    & $NSSMPath set $ServiceName Description "Servicio de sincronización con dispositivos biométricos ZKTeco"
    & $NSSMPath set $ServiceName Start SERVICE_AUTO_START
    
    # Configurar logs
    & $NSSMPath set $ServiceName AppStdout "$logsPath\service-output.log"
    & $NSSMPath set $ServiceName AppStderr "$logsPath\service-error.log"
    & $NSSMPath set $ServiceName AppRotateFiles 1
    & $NSSMPath set $ServiceName AppRotateBytes 10485760
    
    # Configurar recuperación automática
    & $NSSMPath set $ServiceName AppExit Default Restart
    
    Write-Success "Servicio instalado con NSSM"
}
else {
    if ($UseNSSM) {
        Write-Warning "NSSM no encontrado en: $NSSMPath"
        Write-Info "Descarga NSSM desde: https://nssm.cc/download"
        Write-Info "Instalando con sc.exe en su lugar..."
    }
    else {
        Write-Info "Instalando con sc.exe..."
    }
    
    sc.exe create $ServiceName `
        binPath= $exePath `
        start= auto `
        DisplayName= "AttendanceSystem ZKTeco Service" `
        description= "Servicio de sincronización con dispositivos biométricos ZKTeco"
    
    # Configurar recuperación automática
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    Write-Success "Servicio instalado con sc.exe"
}

# Iniciar el servicio
Write-Info "`nIniciando servicio..."
Start-Service -Name $ServiceName

Start-Sleep -Seconds 5

# Verificar estado
$service = Get-Service -Name $ServiceName

Write-Info "`n=== Resultado ==="
Write-Info "Estado del servicio: $($service.Status)"

if ($service.Status -eq "Running") {
    Write-Success "`n✓ Servicio iniciado correctamente"
    Write-Success "El error 1083 ha sido solucionado"
    
    # Verificar puerto gRPC
    Start-Sleep -Seconds 2
    $port = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
    
    if ($null -ne $port) {
        Write-Success "✓ Puerto gRPC 5001 está escuchando"
    }
    else {
        Write-Warning "⚠ Puerto gRPC 5001 no está escuchando aún"
        Write-Info "Espera unos segundos y verifica con: Get-NetTCPConnection -LocalPort 5001"
    }
}
else {
    Write-Error "`n✗ El servicio no pudo iniciarse"
    Write-Info "Revisa los logs en: $logsPath"
    Write-Info "O ejecuta manualmente para ver errores:"
    Write-Info "  cd '$ServicePath'"
    Write-Info "  .\AttendanceSystem.ZKTeco.Service.exe"
}

Write-Info "`nPara ver los logs:"
Write-Info "  Get-Content '$logsPath\*.log' -Tail 50 -Wait"

Write-Info "`nPara verificar el servicio:"
Write-Info "  Get-Service -Name '$ServiceName'"
Write-Info "  Get-NetTCPConnection -LocalPort 5001"
