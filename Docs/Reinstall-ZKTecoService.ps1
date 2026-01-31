# ============================================
# Script de Reinstalación del Servicio ZKTeco
# ============================================
# Este script elimina y reinstala el servicio Windows
# Debe ejecutarse con privilegios de Administrador

$serviceName = "AttendanceSystem.ZKTeco.Service"
$serviceDisplayName = "Sistema de Asistencia - Servicio ZKTeco"
$servicePath = "C:\Services\AttendanceSystem.ZKTeco.Service"
$exePath = "$servicePath\AttendanceSystem.ZKTeco.Service.exe"

Write-Host "=== Reinstalación del Servicio ZKTeco ===" -ForegroundColor Cyan
Write-Host ""

# Verificar privilegios de administrador
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "✗ ERROR: Este script requiere privilegios de Administrador" -ForegroundColor Red
    Write-Host "  Por favor, ejecuta PowerShell como Administrador" -ForegroundColor Yellow
    exit 1
}

# 1. Detener servicio si existe
Write-Host "1. Deteniendo servicio..." -ForegroundColor Yellow
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        try {
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-Host "   ✓ Servicio detenido" -ForegroundColor Green
        }
        catch {
            Write-Host "   ⚠ Error al detener servicio: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "   ℹ Servicio ya estaba detenido" -ForegroundColor Gray
    }
}
else {
    Write-Host "   ℹ Servicio no existe" -ForegroundColor Gray
}

# 2. Eliminar servicio
Write-Host "2. Eliminando servicio..." -ForegroundColor Yellow
if ($service) {
    try {
        $result = sc.exe delete $serviceName
        Start-Sleep -Seconds 2
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✓ Servicio eliminado" -ForegroundColor Green
        }
        else {
            Write-Host "   ⚠ Advertencia al eliminar: $result" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "   ⚠ Error al eliminar servicio: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "   ℹ No hay servicio que eliminar" -ForegroundColor Gray
}

# 3. Verificar que el ejecutable existe
Write-Host "3. Verificando archivos..." -ForegroundColor Yellow
if (Test-Path $exePath) {
    Write-Host "   ✓ Ejecutable encontrado: $exePath" -ForegroundColor Green
    
    # Verificar también el archivo de configuración
    $configPath = "$servicePath\appsettings.Production.json"
    if (Test-Path $configPath) {
        Write-Host "   ✓ Configuración encontrada: appsettings.Production.json" -ForegroundColor Green
    }
    else {
        Write-Host "   ⚠ No se encuentra appsettings.Production.json" -ForegroundColor Yellow
    }
}
else {
    Write-Host "   ✗ ERROR: No se encuentra el ejecutable en $exePath" -ForegroundColor Red
    Write-Host "   Por favor, copia los archivos del servicio primero" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   Pasos sugeridos:" -ForegroundColor Cyan
    Write-Host "   1. Compilar el proyecto:" -ForegroundColor White
    Write-Host "      dotnet publish -c Release -r win-x86 --self-contained true" -ForegroundColor Gray
    Write-Host "   2. Copiar archivos a: $servicePath" -ForegroundColor White
    exit 1
}

# 4. Crear servicio
Write-Host "4. Creando servicio..." -ForegroundColor Yellow
try {
    $createResult = sc.exe create $serviceName binPath= $exePath start= auto DisplayName= $serviceDisplayName
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✓ Servicio creado" -ForegroundColor Green
        
        # Agregar descripción
        sc.exe description $serviceName "Servicio gRPC para comunicación con relojes checadores ZKTeco" | Out-Null
    }
    else {
        Write-Host "   ✗ Error al crear servicio: $createResult" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "   ✗ Error al crear servicio: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 5. Configurar variable de entorno
Write-Host "5. Configurando entorno de producción..." -ForegroundColor Yellow
try {
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    
    # Verificar que la clave existe
    if (Test-Path $regPath) {
        New-ItemProperty -Path $regPath -Name "Environment" -Value "ASPNETCORE_ENVIRONMENT=Production" -PropertyType MultiString -Force | Out-Null
        Write-Host "   ✓ Variable de entorno configurada (ASPNETCORE_ENVIRONMENT=Production)" -ForegroundColor Green
    }
    else {
        Write-Host "   ⚠ No se pudo configurar variable de entorno (clave de registro no encontrada)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "   ⚠ Error al configurar variable de entorno: $($_.Exception.Message)" -ForegroundColor Yellow
}

# 6. Iniciar servicio
Write-Host "6. Iniciando servicio..." -ForegroundColor Yellow
try {
    Start-Service -Name $serviceName -ErrorAction Stop
    Start-Sleep -Seconds 3
    Write-Host "   ✓ Servicio iniciado" -ForegroundColor Green
}
catch {
    Write-Host "   ✗ Error al iniciar servicio: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Revisar Event Viewer para más detalles" -ForegroundColor Yellow
}

# 7. Verificar
Write-Host "7. Verificando estado..." -ForegroundColor Yellow
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    if ($service.Status -eq "Running") {
        Write-Host "   ✓ Servicio ejecutándose correctamente" -ForegroundColor Green
        Write-Host "   - Estado: $($service.Status)" -ForegroundColor Gray
        Write-Host "   - Tipo de inicio: $($service.StartType)" -ForegroundColor Gray
        
        # Verificar puerto
        Start-Sleep -Seconds 2
        $listening = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
        if ($listening) {
            Write-Host "   ✓ Puerto 5001 escuchando (PID: $($listening.OwningProcess))" -ForegroundColor Green
        }
        else {
            Write-Host "   ⚠ Puerto 5001 NO está escuchando" -ForegroundColor Yellow
            Write-Host "   El servicio puede estar iniciando o hay un error" -ForegroundColor Yellow
            Write-Host "   Espera unos segundos y verifica con: netstat -ano | findstr :5001" -ForegroundColor Gray
        }
    }
    else {
        Write-Host "   ✗ ERROR: El servicio no está ejecutándose" -ForegroundColor Red
        Write-Host "   Estado: $($service.Status)" -ForegroundColor Red
        Write-Host ""
        Write-Host "   Pasos de troubleshooting:" -ForegroundColor Cyan
        Write-Host "   1. Ver logs en Event Viewer:" -ForegroundColor White
        Write-Host "      eventvwr.msc" -ForegroundColor Gray
        Write-Host "   2. Intentar ejecutar manualmente:" -ForegroundColor White
        Write-Host "      cd $servicePath" -ForegroundColor Gray
        Write-Host "      .\AttendanceSystem.ZKTeco.Service.exe" -ForegroundColor Gray
    }
}
else {
    Write-Host "   ✗ ERROR: No se puede encontrar el servicio" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Reinstalación Completada ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Próximos pasos:" -ForegroundColor Yellow
Write-Host "1. Verificar que la aplicación Blazor puede conectarse al servicio" -ForegroundColor White
Write-Host "2. Registrar dispositivos desde la interfaz web" -ForegroundColor White
Write-Host "3. Probar conexión a los relojes checadores" -ForegroundColor White
Write-Host ""
Write-Host "Para diagnóstico adicional, ejecutar:" -ForegroundColor Cyan
Write-Host "  .\Docs\Diagnose-AttendanceSystem.ps1" -ForegroundColor Gray
