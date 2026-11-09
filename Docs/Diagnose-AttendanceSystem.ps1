<#
.SYNOPSIS
    Diagnose-AttendanceSystem.ps1 - Diagnóstico de una instalación de AttendanceSystem.
.DESCRIPTION
    Sólo lee: no cambia nada del equipo. Revisa, con los mismos nombres, rutas y puertos que usa el instalador
    (Deploy/setup_script.iss):
      1. El servicio de Windows ZKTeco y su configuración (ApiKey, GrpcPort).
      2. Que el servicio escuche en su puerto gRPC.
      3. El registro COM de 32 bits de zkemkeeper.dll (debe apuntar a la carpeta del servicio).
      4. El sitio web en IIS (si está instalado): sitio, grupo de aplicaciones y enlaces.
      5. El programa de escritorio (si está instalado): su configuración y Hosting:Standalone.
      6. El puerto ADMS y el de PostgreSQL (tomado de la cadena de conexión).
      7. Las reglas de firewall que crea el instalador.
      8. Los últimos errores de las bitácoras de cada parte.
    No muestra contraseñas, cadenas de conexión ni la ApiKey.
    Detalle de cada verificación: Docs/tecnico/06-operacion.md, sección "Script de diagnóstico".
.PARAMETER InstallDir
    Carpeta de la instalación (por omisión, la del instalador).
.PARAMETER AdmsPort
    Puerto ADMS (por omisión, 18373; cambie si lo modificó en la configuración del sistema).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Diagnose-AttendanceSystem.ps1
#>

param(
    [string]$InstallDir = "$env:ProgramFiles\AttendanceSystem",
    [int]$AdmsPort = 18373
)

$ErrorActionPreference = "Continue"
$ServiceName = "AttendanceSystem.ZKTeco.Service"

$script:Passed = 0
$script:Failed = 0
$script:Warnings = 0

function Report-Result {
    param([string]$TestName, [bool]$Success, [string]$Details = "", [bool]$IsWarning = $false)
    if ($Success) {
        Write-Host "  [OK] $TestName" -ForegroundColor Green
        if ($Details) { Write-Host "       $Details" -ForegroundColor DarkGray }
        $script:Passed++
    } elseif ($IsWarning) {
        Write-Host "  [ALERTA] $TestName" -ForegroundColor Yellow
        if ($Details) { Write-Host "       $Details" -ForegroundColor Yellow }
        $script:Warnings++
    } else {
        Write-Host "  [FALLO] $TestName" -ForegroundColor Red
        if ($Details) { Write-Host "       $Details" -ForegroundColor Red }
        $script:Failed++
    }
}

# Lee un JSON sin fallar (los appsettings pueden tener comentarios o estar dañados)
function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try {
        $text = Get-Content -Path $Path -Raw -ErrorAction Stop
        $text = [regex]::Replace($text, '(?m)^\s*//.*$', '')
        return $text | ConvertFrom-Json -ErrorAction Stop
    } catch {
        return "INVALID"
    }
}

# ¿Acepta conexiones el puerto local? Se prueba conectándose (127.0.0.1 y ::1): la lista de puertos en escucha puede
# venir incompleta en sesiones restringidas. Devuelve $null si nadie contesta, o el proceso que escucha si se conoce.
function Test-LocalPort([int]$Port) {
    $open = $false
    foreach ($address in @("127.0.0.1", "::1")) {
        $client = New-Object System.Net.Sockets.TcpClient
        try { $open = $client.ConnectAsync($address, $Port).Wait(2000) -and $client.Connected } catch { $open = $false }
        finally { $client.Dispose() }
        if ($open) { break }
    }
    if (-not $open) { return $null }
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $listener) { return "proceso desconocido" }
    $p = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    if ($p) { return $p.ProcessName } else { return "PID $($listener.OwningProcess)" }
}

$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$ServiceDir = Join-Path $InstallDir "Service"
$WebDir = Join-Path $InstallDir "Web"
$DesktopDir = Join-Path $InstallDir "Desktop"
$webConfig = $null
$desktopConfig = $null

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "   DIAGNÓSTICO DEL SISTEMA - ATTENDANCE SYSTEM      " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Fecha y hora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "Carpeta de instalación: $InstallDir" -ForegroundColor Gray
if (-not (Test-Path $InstallDir)) {
    Write-Host "  [FALLO] La carpeta no existe. Use -InstallDir con la carpeta correcta." -ForegroundColor Red
    exit 1
}

# 1. SERVICIO ZKTECO Y SU CONFIGURACIÓN
Write-Host "`n1. Servicio de Windows ZKTeco..." -ForegroundColor White
$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
if ($service) {
    Report-Result -TestName "Servicio '$ServiceName'" -Success ($service.State -eq "Running") `
        -Details "Estado: $($service.State); inicio: $($service.StartMode); cuenta: $($service.StartName)"
    $expectedExe = Join-Path $ServiceDir "$ServiceName.exe"
    $exeMatches = $service.PathName -and ($service.PathName.Trim('"') -ieq $expectedExe)
    Report-Result -TestName "Ejecutable del servicio" -Success $exeMatches -IsWarning $true `
        -Details "Registrado: $($service.PathName)$(if (-not $exeMatches) { " (se esperaba $expectedExe)" })"
} else {
    Report-Result -TestName "Servicio '$ServiceName'" -Success $false -Details "No está instalado."
}

$grpcPort = 5001
$serviceConfigPath = Join-Path $ServiceDir "appsettings.Production.json"
$serviceConfig = Read-JsonFile $serviceConfigPath
if ($null -eq $serviceConfig) {
    $serviceConfigPath = Join-Path $ServiceDir "appsettings.json"
    $serviceConfig = Read-JsonFile $serviceConfigPath
}
if ($null -eq $serviceConfig) {
    Report-Result -TestName "Configuración del servicio" -Success $false -Details "No existe appsettings.Production.json ni appsettings.json en $ServiceDir."
} elseif ($serviceConfig -eq "INVALID") {
    Report-Result -TestName "Configuración del servicio" -Success $false -Details "$serviceConfigPath no es un JSON válido."
} else {
    $hasKey = -not [string]::IsNullOrWhiteSpace($serviceConfig.ApiKey) -or -not [string]::IsNullOrWhiteSpace($serviceConfig.ZKTecoService.ApiKey)
    Report-Result -TestName "ApiKey del servicio" -Success $hasKey -Details $(if ($hasKey) { "Definida en $serviceConfigPath" } else { "Falta: sin ella toda llamada al servicio falla." })
    if ($serviceConfig.GrpcPort) { $grpcPort = [int]$serviceConfig.GrpcPort }
}

# 2. PUERTO GRPC
Write-Host "`n2. Puerto gRPC del servicio ($grpcPort)..." -ForegroundColor White
$grpc = Test-LocalPort $grpcPort
if ($grpc) {
    Report-Result -TestName "Puerto $grpcPort" -Success $true -Details "Acepta conexiones ($grpc)."
} else {
    Report-Result -TestName "Puerto $grpcPort" -Success $false -Details "Nadie escucha en el puerto $grpcPort."
}

# 3. REGISTRO COM DE ZKEMKEEPER (32 BITS)
Write-Host "`n3. Registro COM del SDK de ZKTeco..." -ForegroundColor White
$clsid = (Get-ItemProperty -Path "Registry::HKEY_CLASSES_ROOT\zkemkeeper.ZKEM\CLSID" -ErrorAction SilentlyContinue).'(default)'
if (-not $clsid) {
    Report-Result -TestName "Clase COM zkemkeeper.ZKEM" -Success $false -Details "No está registrada. Ejecute Reinstall-ZKTecoService.ps1 o el instalador."
} else {
    $dllPath = (Get-ItemProperty -Path "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue).'(default)'
    if (-not $dllPath) {
        Report-Result -TestName "Clase COM zkemkeeper.ZKEM" -Success $false -Details "Registrada ($clsid) pero sin servidor de 32 bits."
    } elseif (-not (Test-Path $dllPath)) {
        Report-Result -TestName "Clase COM zkemkeeper.ZKEM" -Success $false -Details "Apunta a $dllPath, que no existe."
    } else {
        $inServiceDir = (Split-Path $dllPath -Parent) -ieq $ServiceDir
        Report-Result -TestName "Clase COM zkemkeeper.ZKEM" -Success $inServiceDir -IsWarning $true `
            -Details "Registrada en $dllPath$(if (-not $inServiceDir) { ". El instalador la registra en $ServiceDir; otra copia puede cargar DLL de otro SDK." })"
    }
}

# 4. SITIO WEB EN IIS
Write-Host "`n4. Sitio web (IIS)..." -ForegroundColor White
$webInstalled = Test-Path (Join-Path $WebDir "AttendanceSystem.Blazor.Server.dll")
if (-not $webInstalled) {
    Write-Host "  [--] No está instalado en $WebDir." -ForegroundColor DarkGray
} else {
    $appcmd = "$env:windir\System32\inetsrv\appcmd.exe"
    if (-not (Test-Path $appcmd)) {
        Report-Result -TestName "IIS" -Success $false -Details "No existe appcmd.exe: IIS no está habilitado."
    } elseif (-not $IsAdmin) {
        Report-Result -TestName "IIS" -Success $false -IsWarning $true -Details "Sin permiso de administrador no se puede leer IIS. Vuelva a ejecutar el script como administrador."
    } else {
        $site = & $appcmd list site "AttendanceSystem" 2>$null
        if ($LASTEXITCODE -eq 0 -and $site) {
            Report-Result -TestName "Sitio 'AttendanceSystem'" -Success ($site -match "state:Started") -Details "$site"
            $siteHasAdms = $site -match ":$($AdmsPort):"
            Report-Result -TestName "Enlace ADMS en el sitio" -Success $siteHasAdms -IsWarning $true `
                -Details $(if ($siteHasAdms) { "El sitio tiene el puerto $AdmsPort." } else { "El sitio no tiene un enlace en el puerto $AdmsPort." })
        } else {
            Report-Result -TestName "Sitio 'AttendanceSystem'" -Success $false -Details "No existe en IIS."
        }
        $pool = & $appcmd list apppool "AttendanceSystem" 2>$null
        if ($LASTEXITCODE -eq 0 -and $pool) {
            Report-Result -TestName "Grupo de aplicaciones 'AttendanceSystem'" -Success ($pool -match "state:Started") -Details "$pool"
        } else {
            Report-Result -TestName "Grupo de aplicaciones 'AttendanceSystem'" -Success $false -Details "No existe."
        }
    }
    $webConfig = Read-JsonFile (Join-Path $WebDir "appsettings.Production.json")
    if ($null -eq $webConfig) {
        Report-Result -TestName "Configuración del sitio" -Success $false -Details "Falta $WebDir\appsettings.Production.json."
    } elseif ($webConfig -eq "INVALID") {
        Report-Result -TestName "Configuración del sitio" -Success $false -Details "appsettings.Production.json no es un JSON válido."
    } else {
        Report-Result -TestName "Configuración del sitio" -Success (-not [string]::IsNullOrWhiteSpace($webConfig.ConnectionStrings.AttendanceDb)) `
            -Details "ConnectionStrings:AttendanceDb $(if ($webConfig.ConnectionStrings.AttendanceDb) { 'definida' } else { 'FALTA' })"
    }
}

# 5. PROGRAMA DE ESCRITORIO
Write-Host "`n5. Programa de escritorio..." -ForegroundColor White
if (-not (Test-Path (Join-Path $DesktopDir "AttendanceSystem.WPF.exe"))) {
    Write-Host "  [--] No está instalado en $DesktopDir." -ForegroundColor DarkGray
} else {
    $desktopConfig = Read-JsonFile (Join-Path $DesktopDir "appsettings.json")
    if ($null -eq $desktopConfig) {
        Report-Result -TestName "Configuración del programa" -Success $false -Details "Falta $DesktopDir\appsettings.json: el programa no abre."
    } elseif ($desktopConfig -eq "INVALID") {
        Report-Result -TestName "Configuración del programa" -Success $false -Details "appsettings.json no es un JSON válido."
    } else {
        $standalone = [bool]$desktopConfig.Hosting.Standalone
        $consistent = $standalone -ne $webInstalled
        Report-Result -TestName "Hosting:Standalone" -Success $consistent -IsWarning $true `
            -Details "Standalone = $standalone; sitio web instalado = $webInstalled$(if (-not $consistent) { '. Deberían ser opuestos (ver Docs/tecnico/05-instalacion-configuracion.md, sección 4).' })"
    }
}

# 6. PUERTOS ADMS Y POSTGRESQL
Write-Host "`n6. Puertos ADMS y PostgreSQL..." -ForegroundColor White
$adms = Test-LocalPort $AdmsPort
if ($adms) {
    Report-Result -TestName "Puerto ADMS $AdmsPort" -Success $true -Details "Acepta conexiones ($adms; System = IIS, AttendanceSystem.WPF = programa en modo Standalone)."
} else {
    Report-Result -TestName "Puerto ADMS $AdmsPort" -Success $false -IsWarning $true `
        -Details "Nadie escucha: sin sitio web, el programa de escritorio en modo Standalone debe estar abierto."
}

$connection = $null
foreach ($cfg in @($webConfig, $desktopConfig)) {
    if ($cfg -and $cfg -ne "INVALID" -and $cfg.ConnectionStrings.AttendanceDb) { $connection = $cfg.ConnectionStrings.AttendanceDb; break }
}
$dbHost = "localhost"; $dbPort = 5432
if ($connection) {
    if ($connection -match '(?i)(^|;)\s*Host\s*=\s*([^;]+)') { $dbHost = $Matches[2].Trim() }
    if ($connection -match '(?i)(^|;)\s*Port\s*=\s*(\d+)') { $dbPort = [int]$Matches[2] }
}
$db = Test-NetConnection -ComputerName $dbHost -Port $dbPort -WarningAction SilentlyContinue
Report-Result -TestName "PostgreSQL en ${dbHost}:${dbPort}" -Success $db.TcpTestSucceeded `
    -Details $(if ($db.TcpTestSucceeded) { "Conexión TCP exitosa (no comprueba usuario ni contraseña)." } else { "No responde. Revise el servicio postgresql-x64-<versión>." })

# 7. FIREWALL
Write-Host "`n7. Reglas de firewall del instalador..." -ForegroundColor White
foreach ($ruleName in @("AttendanceSystem ADMS", "AttendanceSystem Web")) {
    $rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($rule) {
        $ports = ($rule | Get-NetFirewallPortFilter).LocalPort -join ","
        Report-Result -TestName "Regla '$ruleName'" -Success ($rule.Enabled -eq "True") -Details "Activa: $($rule.Enabled); puertos: $ports"
    } elseif ($ruleName -eq "AttendanceSystem Web" -and -not $webInstalled) {
        Write-Host "  [--] Regla '$ruleName': no aplica sin sitio web." -ForegroundColor DarkGray
    } else {
        Report-Result -TestName "Regla '$ruleName'" -Success $false -IsWarning $true -Details "No existe."
    }
}

# 8. ÚLTIMOS ERRORES EN LAS BITÁCORAS
Write-Host "`n8. Últimos errores en las bitácoras..." -ForegroundColor White
$logSets = @(
    @{ Name = "Servicio ZKTeco"; Dir = Join-Path $ServiceDir "logs\errors"; Filter = "zkteco-errors-*.log" },
    @{ Name = "Sitio web"; Dir = Join-Path $WebDir "logs\errors"; Filter = "errors-*.log" },
    @{ Name = "Sitio web (arranque)"; Dir = Join-Path $WebDir "logs\bootstrap"; Filter = "bootstrap-*.txt" },
    @{ Name = "Programa de escritorio"; Dir = Join-Path $DesktopDir "logs"; Filter = "wpf-*.log" }
)
foreach ($set in $logSets) {
    if (-not (Test-Path $set.Dir)) { continue }
    $latest = Get-ChildItem -Path $set.Dir -Filter $set.Filter -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { continue }
    $errors = Select-String -Path $latest.FullName -Pattern '\[(ERR|FTL)\]' -ErrorAction SilentlyContinue | Select-Object -Last 3
    if ($errors) {
        Write-Host "  [ALERTA] $($set.Name): $($latest.Name) ($($latest.LastWriteTime))" -ForegroundColor Yellow
        foreach ($e in $errors) {
            $line = $e.Line
            if ($line.Length -gt 200) { $line = $line.Substring(0, 200) + "..." }
            Write-Host "       $line" -ForegroundColor DarkGray
        }
        $script:Warnings++
    } else {
        Report-Result -TestName "$($set.Name)" -Success $true -Details "Sin errores en $($latest.Name)."
    }
}

# RESUMEN
Write-Host "`n====================================================" -ForegroundColor Cyan
Write-Host "  Correctas: $script:Passed   Alertas: $script:Warnings   Fallas: $script:Failed" -ForegroundColor $(if ($script:Failed -gt 0) { "Red" } elseif ($script:Warnings -gt 0) { "Yellow" } else { "Green" })
Write-Host "  Qué hacer con cada resultado: Docs/tecnico/06-operacion.md" -ForegroundColor Gray
Write-Host "====================================================" -ForegroundColor Cyan
if ($script:Failed -gt 0) { exit 1 } else { exit 0 }
