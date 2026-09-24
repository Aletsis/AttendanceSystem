<#
.SYNOPSIS
    Diagnose-AttendanceSystem.ps1 - Diagnóstico automatizado para AttendanceSystem.
.DESCRIPTION
    Verifica el estado del servicio Windows ZKTeco, conectividad a PostgreSQL,
    puertos de red (5001, 8081, 5432, 4370), registro de librerías COM de ZKTeco,
    reglas de Firewall y registros recientes de errores.
#>

param(
    [string]$ServicePort = "5001",
    [string]$WebPort = "8081",
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432
)

$ErrorActionPreference = "Continue"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "   DIAGNÓSTICO DEL SISTEMA - ATTENDANCE SYSTEM      " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Fecha y Hora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ""

$Passed = 0
$Failed = 0
$Warnings = 0

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

# 1. VERIFICAR SERVICIO WINDOWS ZKTECO
Write-Host "1. Verificando Servicio Windows ZKTeco..." -ForegroundColor White
$serviceName = "AttendanceSystem.ZKTeco.Service"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    if ($service.Status -eq "Running") {
        Report-Result -TestName "Servicio Windows '$serviceName'" -Success $true -Details "Estado: En ejecución (Running)"
    } else {
        Report-Result -TestName "Servicio Windows '$serviceName'" -Success $false -Details "Estado: $($service.Status). Debe ser 'Running'."
    }
} else {
    Report-Result -TestName "Servicio Windows '$serviceName'" -Success $false -Details "El servicio no está instalado en el sistema."
}

# 2. VERIFICAR PUERTO GRPC (5001)
Write-Host "`n2. Verificando Puerto gRPC ($ServicePort)..." -ForegroundColor White
try {
    $grpcListening = Get-NetTCPConnection -LocalPort $ServicePort -State Listen -ErrorAction SilentlyContinue
    if ($grpcListening) {
        $procId = $grpcListening[0].OwningProcess
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        Report-Result -TestName "Puerto gRPC $ServicePort" -Success $true -Details "Escuchando en proceso: $($proc.ProcessName) (PID: $procId)"
    } else {
        Report-Result -TestName "Puerto gRPC $ServicePort" -Success $false -Details "Ningún proceso está escuchando en el puerto $ServicePort."
    }
} catch {
    Report-Result -TestName "Puerto gRPC $ServicePort" -Success $false -Details $_.Exception.Message
}

# 3. VERIFICAR PUERTO WEB (8081 / 80)
Write-Host "`n3. Verificando Puerto Web de la Aplicación..." -ForegroundColor White
try {
    $webListening = Get-NetTCPConnection -LocalPort $WebPort -State Listen -ErrorAction SilentlyContinue
    if (-not $webListening) {
        $webListening = Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue
        if ($webListening) { $WebPort = "80 (IIS)" }
    }

    if ($webListening) {
        $procId = $webListening[0].OwningProcess
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        Report-Result -TestName "Puerto Web ($WebPort)" -Success $true -Details "Escuchando en proceso: $($proc.ProcessName) (PID: $procId)"
    } else {
        Report-Result -TestName "Puerto Web (8081 / 80)" -Success $false -Details "No se detectó la aplicación web escuchando en el puerto 8081 ni en el puerto 80." -IsWarning $true
    }
} catch {
    Report-Result -TestName "Puerto Web" -Success $false -Details $_.Exception.Message
}

# 4. VERIFICAR CONECTIVIDAD A POSTGRESQL
Write-Host "`n4. Verificando Conectividad a PostgreSQL..." -ForegroundColor White
try {
    $dbConnection = Test-NetConnection -ComputerName $DbHost -Port $DbPort -WarningAction SilentlyContinue
    if ($dbConnection.TcpTestSucceeded) {
        Report-Result -TestName "Conexión a PostgreSQL ($DbHost:$DbPort)" -Success $true -Details "Conexión TCP exitosa."
    } else {
        Report-Result -TestName "Conexión a PostgreSQL ($DbHost:$DbPort)" -Success $false -Details "No se pudo conectar a PostgreSQL en el puerto $DbPort."
    }
} catch {
    Report-Result -TestName "Conexión a PostgreSQL" -Success $false -Details $_.Exception.Message
}

# 5. VERIFICAR REGISTRO DE SDK ZKTECO
Write-Host "`n5. Verificando DLLs y Registro COM de ZKTeco..." -ForegroundColor White
$syswow64Dll = "$env:windir\SysWOW64\zkemkeeper.dll"
$system32Dll = "$env:windir\System32\zkemkeeper.dll"

$dllExists = (Test-Path $syswow64Dll) -or (Test-Path $system32Dll)
if ($dllExists) {
    Report-Result -TestName "Archivo zkemkeeper.dll" -Success $true -Details "Encontrado en carpetas del sistema."
} else {
    Report-Result -TestName "Archivo zkemkeeper.dll" -Success $false -Details "No se encontró zkemkeeper.dll en System32 o SysWOW64."
}

# Verificar Clave COM en Registry
$comRegistered = $false
try {
    $comCheck = Get-ItemProperty -Path "Registry::HKEY_CLASSES_ROOT\zkemkeeper.ZKEM" -ErrorAction SilentlyContinue
    if ($comCheck) { $comRegistered = $true }
} catch {}

if ($comRegistered) {
    Report-Result -TestName "Registro COM (zkemkeeper.ZKEM)" -Success $true -Details "Clase COM registrada correctamente."
} else {
    Report-Result -TestName "Registro COM (zkemkeeper.ZKEM)" -Success $false -Details "La clase COM zkemkeeper.ZKEM no está registrada (ejecutar regsvr32.exe zkemkeeper.dll en x86)."
}

# 6. VERIFICAR REGLAS DE FIREWALL
Write-Host "`n6. Verificando Reglas de Firewall..." -ForegroundColor White
try {
    $firewallRules = Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "*Attendance*" -or $_.DisplayName -like "*ZKTeco*" }
    if ($firewallRules) {
        Report-Result -TestName "Reglas de Firewall para AttendanceSystem" -Success $true -Details "Encontradas $($firewallRules.Count) regla(s) activas."
    } else {
        Report-Result -TestName "Reglas de Firewall" -Success $false -Details "No se encontraron reglas específicas de Firewall configuradas." -IsWarning $true
    }
} catch {
    Report-Result -TestName "Reglas de Firewall" -Success $false -Details $_.Exception.Message -IsWarning $true
}

# 7. VERIFICAR LOGS RECIENTES DE EVENT VIEWER
Write-Host "`n7. Verificando Errores Recientes en Event Viewer..." -ForegroundColor White
try {
    $recentErrors = Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco.Service" -EntryType Error,Warning -Newest 5 -ErrorAction SilentlyContinue
    if ($recentErrors) {
        Write-Host "  [ALERTA] Se encontraron $($recentErrors.Count) evento(s) reciente(s) de advertencia/error:" -ForegroundColor Yellow
        foreach ($evt in $recentErrors) {
            Write-Host "    - [$($evt.TimeGenerated)] $($evt.Message.Substring(0, [Math]::Min(120, $evt.Message.Length)))..." -ForegroundColor DarkGray
        }
        $script:Warnings++
    } else {
        Report-Result -TestName "Logs de Event Viewer" -Success $true -Details "Sin errores recientes del servicio ZKTeco."
    }
} catch {
    Report-Result -TestName "Logs de Event Viewer" -Success $true -Details "Fuente de log lista."
}

# RESUMEN FINAL
Write-Host "`n====================================================" -ForegroundColor Cyan
Write-Host "               RESUMEN DE DIAGNÓSTICO               " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "  Pruebas Exitosas: $Passed" -ForegroundColor Green
Write-Host "  Advertencias:     $Warnings" -ForegroundColor Yellow
Write-Host "  Fallas Críticas:  $Failed" -ForegroundColor $(if ($Failed -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($Failed -eq 0) {
    Write-Host "✅ El sistema se encuentra en un estado operativo correcto." -ForegroundColor Green
} else {
    Write-Host "❌ Se detectaron fallas críticas. Revise los detalles anteriores para corregirlas." -ForegroundColor Red
}
Write-Host "====================================================" -ForegroundColor Cyan
