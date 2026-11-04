<#
.SYNOPSIS
    Reinstall-ZKTecoService.ps1 - Vuelve a registrar el SDK de ZKTeco y el servicio de Windows, como el instalador.
.DESCRIPTION
    Hace lo mismo que Deploy/setup_script.iss para el servicio:
      1. Detiene y elimina el servicio AttendanceSystem.ZKTeco.Service.
      2. Registra zkemkeeper.dll con el regsvr32 de 32 bits DESDE LA CARPETA DEL SERVICIO. No copia DLL a SysWOW64:
         otros programas de ZKTeco dejan ahí DLL con el mismo nombre pero de otro SDK, y pisarlas los rompe.
      3. Crea el servicio con inicio automático y lo inicia.
      4. Comprueba que escuche en su puerto gRPC (GrpcPort de su configuración, 5001 por omisión).
    No toca la configuración (appsettings.Production.json) ni la base de datos. Requiere administrador.
.PARAMETER ServiceDir
    Carpeta del servicio (por omisión, la del instalador).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Reinstall-ZKTecoService.ps1
#>

param(
    [string]$ServiceDir = "$env:ProgramFiles\AttendanceSystem\Service"
)

$ServiceName = "AttendanceSystem.ZKTeco.Service"
# Mismos valores que Deploy/setup_script.iss ([Run], pasos 4 y 5)
$DisplayName = "Attendance System ZKTeco Service"
$Description = "Servicio de comunicación con dispositivos ZKTeco para AttendanceSystem"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: ejecute este script en PowerShell como Administrador." -ForegroundColor Red
    exit 1
}

$serviceExe = Join-Path $ServiceDir "$ServiceName.exe"
$dll = Join-Path $ServiceDir "zkemkeeper.dll"
$regsvr32 = "$env:windir\SysWOW64\regsvr32.exe"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "    REINSTALACIÓN DEL SERVICIO ZKTECO               " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Carpeta del servicio: $ServiceDir" -ForegroundColor Gray

# 0. Comprobar archivos antes de tocar nada
foreach ($file in @($serviceExe, $dll)) {
    if (-not (Test-Path $file)) {
        Write-Host "[ERROR] No existe $file. Use -ServiceDir con la carpeta correcta; no se cambió nada." -ForegroundColor Red
        exit 1
    }
}
if (-not (Test-Path $regsvr32)) {
    Write-Host "[ERROR] No existe $regsvr32 (se necesita Windows de 64 bits)." -ForegroundColor Red
    exit 1
}

# 1. Detener y eliminar el servicio existente
Write-Host "`n1. Deteniendo y eliminando el servicio existente..." -ForegroundColor White
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
    # La eliminación termina cuando nadie tiene abierto el servicio (p. ej. la consola de servicios).
    for ($i = 0; $i -lt 15 -and (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "[ERROR] El servicio sigue registrado (cierre la consola de Servicios y vuelva a intentar)." -ForegroundColor Red
        exit 1
    }
    Write-Host "  - Servicio eliminado." -ForegroundColor Green
} else {
    Write-Host "  - No estaba registrado." -ForegroundColor DarkGray
}

# 2. Registrar el SDK (32 bits) desde la carpeta del servicio
Write-Host "`n2. Registrando zkemkeeper.dll (32 bits) desde la carpeta del servicio..." -ForegroundColor White
$reg = Start-Process -FilePath $regsvr32 -ArgumentList "/s `"$dll`"" -Wait -PassThru
if ($reg.ExitCode -ne 0) {
    Write-Host "[ERROR] regsvr32 terminó con código $($reg.ExitCode)." -ForegroundColor Red
    Write-Host "        Código 3: falta una dependencia; revise que las DLL del SDK estén junto a zkemkeeper.dll." -ForegroundColor Yellow
    exit 1
}
Write-Host "  - Registrado." -ForegroundColor Green

# 3. Crear e iniciar el servicio
Write-Host "`n3. Creando e iniciando el servicio..." -ForegroundColor White
$create = & sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto displayname= $DisplayName
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] sc create falló: $create" -ForegroundColor Red
    exit 1
}
& sc.exe description $ServiceName $Description | Out-Null
Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
try {
    (Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(15))
    Write-Host "  - Servicio en ejecución." -ForegroundColor Green
} catch {
    Write-Host "[ALERTA] El servicio no llegó a Running. Revise $ServiceDir\logs\errors\ y $ServiceDir\logs\bootstrap\." -ForegroundColor Yellow
}

# 4. Comprobar el puerto gRPC
$port = 5001
foreach ($name in @("appsettings.Production.json", "appsettings.json")) {
    $cfgPath = Join-Path $ServiceDir $name
    if (Test-Path $cfgPath) {
        try {
            $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
            if ($cfg.GrpcPort) { $port = [int]$cfg.GrpcPort }
            if (-not $cfg.ApiKey) { Write-Host "[ALERTA] $name no tiene ApiKey: toda llamada al servicio fallará." -ForegroundColor Yellow }
        } catch { Write-Host "[ALERTA] $name no es un JSON válido." -ForegroundColor Yellow }
        break
    }
}
Write-Host "`n4. Comprobando el puerto gRPC $port..." -ForegroundColor White
$listening = $null
for ($i = 0; $i -lt 10 -and -not $listening; $i++) {
    $listening = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if (-not $listening) { Start-Sleep -Seconds 1 }
}
if ($listening) {
    Write-Host "  - Puerto $port escuchando." -ForegroundColor Green
} else {
    Write-Host "[ALERTA] Nadie escucha en el puerto $port. Revise las bitácoras del servicio." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n====================================================" -ForegroundColor Cyan
Write-Host "Reinstalación terminada." -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Cyan
