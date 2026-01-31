# Script de Despliegue Automatizado para AttendanceSystem en IIS
# Ejecutar como Administrador

param(
    [string]$PublishPath = "C:\Publish\AttendanceSystem",
    [string]$SiteName = "AttendanceSystem",
    [string]$AppPoolName = "AttendanceSystemPool",
    [int]$HttpPort = 80,
    [int]$HttpsPort = 443,
    [string]$HostName = "",
    [switch]$SkipPublish,
    [switch]$SkipIISConfig
)

# Colores para output
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

Write-Info "=== Iniciando Despliegue de AttendanceSystem ==="

# 1. Verificar que IIS está instalado
Write-Info "`n[1/8] Verificando IIS..."
$iisFeature = Get-WindowsFeature -Name Web-Server -ErrorAction SilentlyContinue
if ($null -eq $iisFeature -or $iisFeature.InstallState -ne "Installed") {
    Write-Warning "IIS no está instalado. Instalando..."
    Install-WindowsFeature -name Web-Server -IncludeManagementTools
    Install-WindowsFeature -name Web-Asp-Net45
    Install-WindowsFeature -name Web-WebSockets
    Write-Success "IIS instalado correctamente"
} else {
    Write-Success "IIS ya está instalado"
}

# 2. Verificar ASP.NET Core Hosting Bundle
Write-Info "`n[2/8] Verificando ASP.NET Core Hosting Bundle..."
$ancmModule = Get-WebGlobalModule -Name "AspNetCoreModuleV2" -ErrorAction SilentlyContinue
if ($null -eq $ancmModule) {
    Write-Warning "ASP.NET Core Module V2 no encontrado"
    Write-Warning "Por favor, descarga e instala el ASP.NET Core Hosting Bundle desde:"
    Write-Warning "https://dotnet.microsoft.com/download/dotnet/8.0"
    $continue = Read-Host "¿Continuar de todas formas? (s/n)"
    if ($continue -ne "s") { exit 1 }
} else {
    Write-Success "ASP.NET Core Module V2 encontrado"
}

# 3. Publicar la aplicación
if (-not $SkipPublish) {
    Write-Info "`n[3/8] Publicando aplicación..."
    
    # Buscar el archivo .csproj del proyecto Blazor Server
    $projectPath = Get-ChildItem -Path "." -Filter "AttendanceSystem.Blazor.Server.csproj" -Recurse | Select-Object -First 1
    
    if ($null -eq $projectPath) {
        Write-Error "No se encontró el proyecto AttendanceSystem.Blazor.Server.csproj"
        exit 1
    }
    
    Write-Info "Proyecto encontrado: $($projectPath.FullName)"
    
    # Crear carpeta temporal para publicación
    $tempPublishPath = "$env:TEMP\AttendanceSystem_Publish_$(Get-Date -Format 'yyyyMMddHHmmss')"
    
    # Publicar
    dotnet publish $projectPath.FullName -c Release -o $tempPublishPath --runtime win-x64 --self-contained false
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Error al publicar la aplicación"
        exit 1
    }
    
    Write-Success "Aplicación publicada en: $tempPublishPath"
    
    # Crear backup si existe versión anterior
    if (Test-Path $PublishPath) {
        $backupPath = "C:\Backups\AttendanceSystem_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Write-Info "Creando backup en: $backupPath"
        New-Item -Path "C:\Backups" -ItemType Directory -Force | Out-Null
        Copy-Item -Path $PublishPath -Destination $backupPath -Recurse -Force
        Write-Success "Backup creado"
    }
    
    # Copiar archivos publicados
    Write-Info "Copiando archivos a: $PublishPath"
    New-Item -Path $PublishPath -ItemType Directory -Force | Out-Null
    Copy-Item -Path "$tempPublishPath\*" -Destination $PublishPath -Recurse -Force
    
    # Limpiar carpeta temporal
    Remove-Item -Path $tempPublishPath -Recurse -Force
    
    Write-Success "Archivos copiados correctamente"
} else {
    Write-Warning "Omitiendo publicación (SkipPublish activado)"
}

# 4. Configurar IIS
if (-not $SkipIISConfig) {
    Write-Info "`n[4/8] Configurando IIS..."
    
    Import-Module WebAdministration
    
    # Crear Application Pool si no existe
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        Write-Info "Creando Application Pool: $AppPoolName"
        New-WebAppPool -Name $AppPoolName
        Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
        Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "startMode" -Value "AlwaysRunning"
        Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "processModel.idleTimeout" -Value ([TimeSpan]::FromMinutes(0))
        Write-Success "Application Pool creado"
    } else {
        Write-Success "Application Pool ya existe"
    }
    
    # Detener el sitio si existe
    if (Test-Path "IIS:\Sites\$SiteName") {
        Write-Info "Deteniendo sitio existente..."
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        
        # Eliminar sitio existente
        Remove-Website -Name $SiteName
        Write-Success "Sitio existente eliminado"
    }
    
    # Crear sitio web
    Write-Info "Creando sitio web: $SiteName"
    
    if ($HostName -ne "") {
        New-Website -Name $SiteName -PhysicalPath $PublishPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $HostName
    } else {
        New-Website -Name $SiteName -PhysicalPath $PublishPath -ApplicationPool $AppPoolName -Port $HttpPort
    }
    
    Write-Success "Sitio web creado"
    
    # Agregar binding HTTPS si se especificó
    if ($HttpsPort -ne 0) {
        Write-Info "Agregando binding HTTPS en puerto $HttpsPort"
        # Nota: Necesitarás configurar el certificado SSL manualmente
        Write-Warning "Recuerda configurar el certificado SSL en IIS Manager"
    }
    
} else {
    Write-Warning "Omitiendo configuración de IIS (SkipIISConfig activado)"
}

# 5. Configurar permisos
Write-Info "`n[5/8] Configurando permisos..."

$acl = Get-Acl $PublishPath
$permission = "IIS AppPool\$AppPoolName", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $PublishPath $acl

Write-Success "Permisos configurados"

# 6. Crear carpeta de logs
Write-Info "`n[6/8] Configurando logs..."

$logsPath = Join-Path $PublishPath "logs"
New-Item -Path $logsPath -ItemType Directory -Force | Out-Null

$acl = Get-Acl $logsPath
$permission = "IIS AppPool\$AppPoolName", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $logsPath $acl

Write-Success "Carpeta de logs configurada"

# 7. Configurar Firewall
Write-Info "`n[7/8] Configurando Firewall..."

$httpRule = Get-NetFirewallRule -DisplayName "AttendanceSystem HTTP" -ErrorAction SilentlyContinue
if ($null -eq $httpRule) {
    New-NetFirewallRule -DisplayName "AttendanceSystem HTTP" -Direction Inbound -LocalPort $HttpPort -Protocol TCP -Action Allow | Out-Null
    Write-Success "Regla de firewall HTTP creada"
} else {
    Write-Success "Regla de firewall HTTP ya existe"
}

if ($HttpsPort -ne 0) {
    $httpsRule = Get-NetFirewallRule -DisplayName "AttendanceSystem HTTPS" -ErrorAction SilentlyContinue
    if ($null -eq $httpsRule) {
        New-NetFirewallRule -DisplayName "AttendanceSystem HTTPS" -Direction Inbound -LocalPort $HttpsPort -Protocol TCP -Action Allow | Out-Null
        Write-Success "Regla de firewall HTTPS creada"
    } else {
        Write-Success "Regla de firewall HTTPS ya existe"
    }
}

# 8. Iniciar el sitio
if (-not $SkipIISConfig) {
    Write-Info "`n[8/8] Iniciando sitio..."
    
    Start-WebAppPool -Name $AppPoolName
    Start-Website -Name $SiteName
    
    Start-Sleep -Seconds 2
    
    # Verificar que está corriendo
    $site = Get-Website -Name $SiteName
    if ($site.State -eq "Started") {
        Write-Success "Sitio iniciado correctamente"
    } else {
        Write-Error "El sitio no pudo iniciarse. Estado: $($site.State)"
    }
}

# Resumen
Write-Info "`n=== Resumen del Despliegue ==="
Write-Info "Ruta de publicación: $PublishPath"
Write-Info "Sitio IIS: $SiteName"
Write-Info "Application Pool: $AppPoolName"
Write-Info "Puerto HTTP: $HttpPort"
if ($HttpsPort -ne 0) { Write-Info "Puerto HTTPS: $HttpsPort" }
if ($HostName -ne "") { Write-Info "Host Name: $HostName" }

Write-Success "`n✓ Despliegue completado exitosamente"

# Probar el sitio
Write-Info "`nProbando el sitio..."
try {
    $response = Invoke-WebRequest -Uri "http://localhost:$HttpPort" -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -eq 200) {
        Write-Success "✓ El sitio responde correctamente (HTTP 200)"
    } else {
        Write-Warning "El sitio respondió con código: $($response.StatusCode)"
    }
} catch {
    Write-Warning "No se pudo probar el sitio: $($_.Exception.Message)"
    Write-Info "Revisa los logs en: $logsPath"
}

Write-Info "`nAccede al sitio en: http://localhost:$HttpPort"
if ($HostName -ne "") {
    Write-Info "O en: http://$HostName:$HttpPort"
}

Write-Info "`nPara ver los logs:"
Write-Info "  - Application logs: $logsPath"
Write-Info "  - IIS logs: C:\inetpub\logs\LogFiles\"
Write-Info "  - Event Viewer: Windows Logs → Application"
