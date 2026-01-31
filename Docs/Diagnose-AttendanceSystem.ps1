# Script de Diagnóstico para AttendanceSystem
# Este script verifica la configuración y conectividad del sistema

Write-Host "=== Diagnóstico de AttendanceSystem ===" -ForegroundColor Cyan
Write-Host ""

# 1. Verificar Servicio Windows
Write-Host "1. Verificando Servicio ZKTeco..." -ForegroundColor Yellow
$serviceName = "AttendanceSystem.ZKTeco.Service"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    Write-Host "   ✓ Servicio encontrado: $($service.Status)" -ForegroundColor Green
    if ($service.Status -ne "Running") {
        Write-Host "   ⚠ El servicio NO está ejecutándose" -ForegroundColor Red
        Write-Host "   Intentar iniciar con: sc.exe start '$serviceName'" -ForegroundColor Yellow
    }
}
else {
    Write-Host "   ✗ Servicio NO encontrado" -ForegroundColor Red
    Write-Host "   El servicio debe ser instalado primero" -ForegroundColor Yellow
}
Write-Host ""

# 2. Verificar Puerto gRPC
Write-Host "2. Verificando Puerto gRPC (5001)..." -ForegroundColor Yellow
$port = 5001
$listening = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue

if ($listening) {
    Write-Host "   ✓ Puerto $port está escuchando" -ForegroundColor Green
    Write-Host "   PID: $($listening.OwningProcess)" -ForegroundColor Gray
}
else {
    Write-Host "   ✗ Puerto $port NO está escuchando" -ForegroundColor Red
    Write-Host "   El servicio ZKTeco debe estar ejecutándose" -ForegroundColor Yellow
}
Write-Host ""

# 3. Verificar Firewall
Write-Host "3. Verificando Reglas de Firewall..." -ForegroundColor Yellow
$firewallRule = Get-NetFirewallRule -DisplayName "*ZKTeco*" -ErrorAction SilentlyContinue

if ($firewallRule) {
    Write-Host "   ✓ Regla de firewall encontrada" -ForegroundColor Green
    foreach ($rule in $firewallRule) {
        Write-Host "   - $($rule.DisplayName): $($rule.Enabled)" -ForegroundColor Gray
    }
}
else {
    Write-Host "   ⚠ No se encontraron reglas de firewall para ZKTeco" -ForegroundColor Yellow
    Write-Host "   Puede ser necesario crear una regla si hay problemas de conectividad" -ForegroundColor Yellow
}
Write-Host ""

# 4. Leer Configuración
Write-Host "4. Verificando Configuración..." -ForegroundColor Yellow

# Buscar archivos de configuración
$blazorConfig = "src\Presentation\AttendanceSystem.Blazor.Server\appsettings.json"
$zktecoConfig = "src\Presentation\AttendanceSystem.ZKTeco.Service\appsettings.json"

if (Test-Path $blazorConfig) {
    $config = Get-Content $blazorConfig | ConvertFrom-Json
    $zktecoUrl = $config.ZKTecoService.Url
    Write-Host "   Blazor -> ZKTeco URL: $zktecoUrl" -ForegroundColor Gray
    
    if ($zktecoUrl -like "https://*") {
        Write-Host "   ⚠ ADVERTENCIA: Usando HTTPS pero el servicio usa HTTP" -ForegroundColor Red
        Write-Host "   Cambiar a http:// en appsettings.json" -ForegroundColor Yellow
    }
    else {
        Write-Host "   ✓ Protocolo correcto (HTTP)" -ForegroundColor Green
    }
}
else {
    Write-Host "   ⚠ No se encontró $blazorConfig" -ForegroundColor Yellow
}

if (Test-Path $zktecoConfig) {
    $config = Get-Content $zktecoConfig | ConvertFrom-Json
    $grpcPort = $config.GrpcPort
    
    Write-Host "   ZKTeco Service:" -ForegroundColor Gray
    Write-Host "   - Puerto gRPC: $grpcPort" -ForegroundColor Gray
    Write-Host "   - Dispositivos: Se gestionan desde la base de datos" -ForegroundColor Gray
}
else {
    Write-Host "   ⚠ No se encontró $zktecoConfig" -ForegroundColor Yellow
}
Write-Host ""

# 5. Verificar Base de Datos
Write-Host "5. Verificando Conexión a PostgreSQL..." -ForegroundColor Yellow

if (Test-Path $blazorConfig) {
    $config = Get-Content $blazorConfig | ConvertFrom-Json
    $connString = $config.ConnectionStrings.AttendanceDb
    
    if ($connString -match "Host=([^;]+)") {
        $dbHost = $matches[1]
        Write-Host "   Host PostgreSQL: $dbHost" -ForegroundColor Gray
        
        $dbPing = Test-Connection -ComputerName $dbHost -Count 2 -Quiet -ErrorAction SilentlyContinue
        if ($dbPing) {
            Write-Host "   ✓ Servidor PostgreSQL accesible" -ForegroundColor Green
        }
        else {
            Write-Host "   ✗ No se puede alcanzar el servidor PostgreSQL" -ForegroundColor Red
        }
    }
}
Write-Host ""

# 6. Nota sobre Dispositivos
Write-Host "6. Gestión de Dispositivos..." -ForegroundColor Yellow
Write-Host "   ℹ Los relojes checadores se registran en la base de datos" -ForegroundColor Cyan
Write-Host "   ℹ Acceder a la aplicación web para registrar dispositivos" -ForegroundColor Cyan
Write-Host "   ℹ Cada dispositivo tiene: ID, Nombre, IP, Puerto, Ubicación" -ForegroundColor Cyan
Write-Host ""

# 7. Verificar Logs Recientes
Write-Host "7. Verificando Logs Recientes..." -ForegroundColor Yellow

$logPath = "src\Presentation\AttendanceSystem.Blazor.Server\logs"
if (Test-Path $logPath) {
    $recentLogs = Get-ChildItem -Path $logPath -Filter "*.log" | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object -First 3
    
    if ($recentLogs) {
        Write-Host "   Logs recientes encontrados:" -ForegroundColor Gray
        foreach ($log in $recentLogs) {
            Write-Host "   - $($log.Name) ($(Get-Date $log.LastWriteTime -Format 'yyyy-MM-dd HH:mm'))" -ForegroundColor Gray
        }
        
        # Buscar errores recientes
        $latestLog = $recentLogs[0]
        $errors = Get-Content $latestLog.FullName -Tail 50 | Select-String -Pattern "ERROR|Exception" -SimpleMatch
        
        if ($errors) {
            Write-Host "   ⚠ Se encontraron errores recientes:" -ForegroundColor Yellow
            $errors | Select-Object -First 5 | ForEach-Object {
                Write-Host "   $($_.Line.Substring(0, [Math]::Min(100, $_.Line.Length)))" -ForegroundColor Red
            }
        }
        else {
            Write-Host "   ✓ No se encontraron errores en los últimos 50 registros" -ForegroundColor Green
        }
    }
}
else {
    Write-Host "   ⚠ Directorio de logs no encontrado: $logPath" -ForegroundColor Yellow
}
Write-Host ""

# Resumen
Write-Host "=== Resumen ===" -ForegroundColor Cyan
Write-Host "Si hay problemas de conexión, verificar:" -ForegroundColor Yellow
Write-Host "1. El servicio ZKTeco está ejecutándose" -ForegroundColor White
Write-Host "2. El puerto 5001 está escuchando" -ForegroundColor White
Write-Host "3. La URL usa http:// (no https://)" -ForegroundColor White
Write-Host "4. Los dispositivos están registrados en la base de datos" -ForegroundColor White
Write-Host "5. PostgreSQL está ejecutándose y accesible" -ForegroundColor White
Write-Host ""
Write-Host "Para más información, consultar: Docs\DEPLOYMENT_GUIDE.md" -ForegroundColor Cyan
