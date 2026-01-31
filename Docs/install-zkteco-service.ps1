# Script de Instalación del Servicio ZKTeco
# Ejecutar como Administrador

param(
    [string]$ServicePath = "C:\Services\AttendanceSystem.ZKTeco",
    [string]$ServiceName = "AttendanceSystem.ZKTeco",
    [string]$DisplayName = "AttendanceSystem ZKTeco Service",
    [int]$GrpcPort = 5001,
    [string]$DeviceIP = "192.168.1.100",
    [int]$DevicePort = 4370,
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

Write-Info "=== Instalación de AttendanceSystem.ZKTeco.Service ==="

# 1. Verificar .NET Runtime x86
Write-Info "`n[1/8] Verificando .NET Runtime x86..."
$runtimes = dotnet --list-runtimes 2>$null
$hasX86Runtime = $runtimes | Where-Object { $_ -like "*Microsoft.NETCore.App*" -and $_ -like "*x86*" }

if ($null -eq $hasX86Runtime) {
    Write-Warning ".NET Runtime x86 no encontrado"
    Write-Warning "Descarga e instala desde: https://dotnet.microsoft.com/download/dotnet/9.0"
    Write-Warning "IMPORTANTE: Descarga la versión x86 (32 bits)"
    
    $continue = Read-Host "¿Continuar de todas formas? (s/n)"
    if ($continue -ne "s") { exit 1 }
}
else {
    Write-Success ".NET Runtime x86 encontrado"
}

# 2. Publicar el servicio
Write-Info "`n[2/8] Publicando servicio..."

# Buscar el proyecto
$projectPath = Get-ChildItem -Path "." -Filter "AttendanceSystem.ZKTeco.Service.csproj" -Recurse | Select-Object -First 1

if ($null -eq $projectPath) {
    Write-Error "No se encontró el proyecto AttendanceSystem.ZKTeco.Service.csproj"
    Write-Info "Asegúrate de ejecutar este script desde la carpeta del repositorio"
    exit 1
}

Write-Info "Proyecto encontrado: $($projectPath.FullName)"

# Crear carpeta temporal para publicación
$tempPublishPath = "$env:TEMP\ZKTecoService_Publish_$(Get-Date -Format 'yyyyMMddHHmmss')"

# Publicar (IMPORTANTE: win-x86 porque el SDK ZKTeco es 32 bits)
Write-Info "Publicando para win-x86 (32 bits)..."
dotnet publish $projectPath.FullName -c Release -o $tempPublishPath --runtime win-x86 --self-contained true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error al publicar el servicio"
    exit 1
}

Write-Success "Servicio publicado en: $tempPublishPath"

# 3. Crear backup si existe versión anterior
if (Test-Path $ServicePath) {
    Write-Info "`n[3/8] Creando backup de versión anterior..."
    
    # Detener servicio si existe
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $existingService) {
        Write-Info "Deteniendo servicio existente..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
    }
    
    $backupPath = "C:\Backups\ZKTecoService_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    New-Item -Path "C:\Backups" -ItemType Directory -Force | Out-Null
    Copy-Item -Path $ServicePath -Destination $backupPath -Recurse -Force
    Write-Success "Backup creado en: $backupPath"
}
else {
    Write-Info "`n[3/8] No hay versión anterior, omitiendo backup"
}

# 4. Copiar archivos
Write-Info "`n[4/8] Copiando archivos del servicio..."

New-Item -Path $ServicePath -ItemType Directory -Force | Out-Null
Copy-Item -Path "$tempPublishPath\*" -Destination $ServicePath -Recurse -Force

# Limpiar carpeta temporal
Remove-Item -Path $tempPublishPath -Recurse -Force

Write-Success "Archivos copiados a: $ServicePath"

# 5. Crear configuración
Write-Info "`n[5/8] Creando archivo de configuración..."

$appsettingsPath = Join-Path $ServicePath "appsettings.json"

# Solo crear si no existe (para no sobrescribir configuración existente)
if (-not (Test-Path $appsettingsPath)) {
    $appsettings = @{
        Logging      = @{
            LogLevel = @{
                Default                = "Information"
                "Microsoft.AspNetCore" = "Warning"
                Grpc                   = "Information"
            }
        }
        GrpcPort     = $GrpcPort
        ZKTeco       = @{
            DeviceIP                 = $DeviceIP
            DevicePort               = $DevicePort
            ConnectionTimeout        = 30
            ReadTimeout              = 30
            AutoReconnect            = $true
            ReconnectIntervalSeconds = 60
        }
        AllowedHosts = "*"
    }
    
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content -Path $appsettingsPath -Encoding UTF8
    Write-Success "Configuración creada: $appsettingsPath"
}
else {
    Write-Success "Configuración existente preservada"
}

# 6. Crear carpeta de logs
Write-Info "`n[6/8] Configurando logs..."

$logsPath = Join-Path $ServicePath "logs"
New-Item -Path $logsPath -ItemType Directory -Force | Out-Null

# Dar permisos
$acl = Get-Acl $logsPath
$permission = "NT AUTHORITY\LOCAL SERVICE", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $logsPath $acl

Write-Success "Carpeta de logs configurada: $logsPath"

# 7. Instalar servicio
Write-Info "`n[7/8] Instalando servicio de Windows..."

$exePath = Join-Path $ServicePath "AttendanceSystem.ZKTeco.Service.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "No se encontró el ejecutable: $exePath"
    exit 1
}

# Eliminar servicio existente si existe
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    Write-Info "Eliminando servicio existente..."
    
    if ($UseNSSM -and (Test-Path $NSSMPath)) {
        & $NSSMPath stop $ServiceName
        & $NSSMPath remove $ServiceName confirm
    }
    else {
        sc.exe stop $ServiceName
        sc.exe delete $ServiceName
    }
    
    Start-Sleep -Seconds 2
}

if ($UseNSSM) {
    # Usar NSSM
    if (-not (Test-Path $NSSMPath)) {
        Write-Warning "NSSM no encontrado en: $NSSMPath"
        Write-Info "Descarga NSSM desde: https://nssm.cc/download"
        Write-Info "Extrae a: C:\Tools\nssm"
        
        $useScExe = Read-Host "¿Usar sc.exe en su lugar? (s/n)"
        if ($useScExe -ne "s") { exit 1 }
        $UseNSSM = $false
    }
}

if ($UseNSSM -and (Test-Path $NSSMPath)) {
    Write-Info "Instalando con NSSM..."
    
    & $NSSMPath install $ServiceName $exePath
    & $NSSMPath set $ServiceName AppDirectory $ServicePath
    & $NSSMPath set $ServiceName DisplayName $DisplayName
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
    Write-Info "Instalando con sc.exe..."
    
    sc.exe create $ServiceName `
        binPath= $exePath `
        start= auto `
        DisplayName= $DisplayName `
        description= "Servicio de sincronización con dispositivos biométricos ZKTeco"
    
    # Configurar recuperación automática
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    Write-Success "Servicio instalado con sc.exe"
}

# 8. Configurar firewall
Write-Info "`n[8/8] Configurando firewall..."

# Puerto gRPC
$grpcRule = Get-NetFirewallRule -DisplayName "AttendanceSystem ZKTeco gRPC" -ErrorAction SilentlyContinue
if ($null -eq $grpcRule) {
    New-NetFirewallRule -DisplayName "AttendanceSystem ZKTeco gRPC" `
        -Direction Inbound `
        -LocalPort $GrpcPort `
        -Protocol TCP `
        -Action Allow | Out-Null
    Write-Success "Regla de firewall gRPC creada (puerto $GrpcPort)"
}
else {
    Write-Success "Regla de firewall gRPC ya existe"
}

# Puerto ZKTeco
$zktecoRule = Get-NetFirewallRule -DisplayName "ZKTeco Devices" -ErrorAction SilentlyContinue
if ($null -eq $zktecoRule) {
    New-NetFirewallRule -DisplayName "ZKTeco Devices" `
        -Direction Inbound `
        -LocalPort $DevicePort `
        -Protocol TCP `
        -Action Allow | Out-Null
    
    New-NetFirewallRule -DisplayName "ZKTeco Devices Outbound" `
        -Direction Outbound `
        -RemotePort $DevicePort `
        -Protocol TCP `
        -Action Allow | Out-Null
    
    Write-Success "Reglas de firewall ZKTeco creadas (puerto $DevicePort)"
}
else {
    Write-Success "Reglas de firewall ZKTeco ya existen"
}

# Iniciar el servicio
Write-Info "`nIniciando servicio..."
Start-Service -Name $ServiceName

Start-Sleep -Seconds 3

# Verificar estado
$service = Get-Service -Name $ServiceName

Write-Info "`n=== Resumen de la Instalación ==="
Write-Info "Nombre del servicio: $ServiceName"
Write-Info "Ruta de instalación: $ServicePath"
Write-Info "Puerto gRPC: $GrpcPort"
Write-Info "Dispositivo ZKTeco: $DeviceIP:$DevicePort"
Write-Info "Estado del servicio: $($service.Status)"

if ($service.Status -eq "Running") {
    Write-Success "`n✓ Instalación completada exitosamente"
    
    # Verificar puerto
    Start-Sleep -Seconds 2
    $port = Get-NetTCPConnection -LocalPort $GrpcPort -State Listen -ErrorAction SilentlyContinue
    
    if ($null -ne $port) {
        Write-Success "✓ Puerto gRPC $GrpcPort está escuchando"
    }
    else {
        Write-Warning "⚠ Puerto gRPC $GrpcPort no está escuchando aún"
        Write-Info "Espera unos segundos y verifica con: Get-NetTCPConnection -LocalPort $GrpcPort"
    }
}
else {
    Write-Error "`n✗ El servicio no pudo iniciarse"
    Write-Info "Revisa los logs en: $logsPath"
    Write-Info "O ejecuta manualmente para ver errores: $exePath"
}

Write-Info "`nPara ver los logs:"
Write-Info "  Get-Content '$logsPath\*.log' -Tail 50 -Wait"

Write-Info "`nPara gestionar el servicio:"
Write-Info "  Get-Service -Name '$ServiceName'"
Write-Info "  Start-Service -Name '$ServiceName'"
Write-Info "  Stop-Service -Name '$ServiceName'"
Write-Info "  Restart-Service -Name '$ServiceName'"

Write-Info "`nPara probar la conexión gRPC:"
Write-Info "  Test-NetConnection -ComputerName localhost -Port $GrpcPort"

Write-Info "`nPara probar la conexión al dispositivo:"
Write-Info "  Test-NetConnection -ComputerName $DeviceIP -Port $DevicePort"
