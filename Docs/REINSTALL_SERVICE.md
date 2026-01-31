# Guía de Reinstalación del Servicio ZKTeco

Esta guía te ayudará a eliminar el servicio Windows existente y reinstalarlo con la configuración corregida.

## Paso 1: Detener el Servicio Actual

Primero, detén el servicio si está ejecutándose:

```powershell
# Detener el servicio
sc.exe stop "AttendanceSystem.ZKTeco.Service"

# O usando PowerShell
Stop-Service -Name "AttendanceSystem.ZKTeco.Service" -Force
```

**Verificar que se detuvo**:
```powershell
Get-Service "AttendanceSystem.ZKTeco.Service"
```

Deberías ver `Status: Stopped`

## Paso 2: Eliminar el Servicio

```powershell
# Eliminar el servicio
sc.exe delete "AttendanceSystem.ZKTeco.Service"
```

**Salida esperada**:
```
[SC] DeleteService SUCCESS
```

**Verificar que se eliminó**:
```powershell
Get-Service "AttendanceSystem.ZKTeco.Service" -ErrorAction SilentlyContinue
```

No debería encontrar el servicio (error esperado).

## Paso 3: Limpiar Archivos Antiguos (Opcional pero Recomendado)

Si quieres asegurarte de que no quedan archivos antiguos:

```powershell
# Navegar al directorio donde está instalado el servicio
cd "C:\Path\To\AttendanceSystem.ZKTeco.Service"

# Hacer backup del directorio actual (opcional)
# Compress-Archive -Path . -DestinationPath "C:\Backups\ZKTecoService_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"

# Eliminar archivos antiguos
Remove-Item -Path * -Recurse -Force
```

## Paso 4: Copiar Archivos Nuevos

### Opción A: Desde Publicación Local

Si compilaste el proyecto localmente:

```powershell
# Publicar el proyecto (desde el directorio raíz del proyecto)
cd "C:\Users\B10 Caja 2\source\repos\AttendanceSystem"

dotnet publish src/Presentation/AttendanceSystem.ZKTeco.Service/AttendanceSystem.ZKTeco.Service.csproj `
  -c Release `
  -r win-x86 `
  --self-contained true `
  -o "C:\Publish\ZKTecoService"
```

Luego copia los archivos al servidor:

```powershell
# En el servidor de producción
# Copiar desde la ubicación de publicación
Copy-Item -Path "\\SERVIDOR_ORIGEN\Publish\ZKTecoService\*" `
          -Destination "C:\Services\AttendanceSystem.ZKTeco.Service\" `
          -Recurse -Force
```

### Opción B: Copiar Directamente

Si ya tienes los archivos compilados:

```powershell
# Copiar archivos al servidor
xcopy /E /I /Y "\\SERVIDOR_ORIGEN\ZKTecoService\*" "C:\Services\AttendanceSystem.ZKTeco.Service\"
```

## Paso 5: Actualizar Configuración

Edita el archivo `appsettings.Production.json` en el servidor:

```powershell
# Navegar al directorio del servicio
cd "C:\Services\AttendanceSystem.ZKTeco.Service"

# Editar el archivo de configuración
notepad appsettings.Production.json
```

**Contenido correcto** (sin configuración de dispositivos):

```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft.AspNetCore": "Warning",
            "Grpc": "Information"
        }
    },
    "GrpcPort": 5001,
    "AllowedHosts": "*"
}
```

## Paso 6: Crear el Servicio Nuevamente

```powershell
# Crear el servicio
sc.exe create "AttendanceSystem.ZKTeco.Service" `
  binPath= "C:\Services\AttendanceSystem.ZKTeco.Service\AttendanceSystem.ZKTeco.Service.exe" `
  start= auto `
  DisplayName= "Sistema de Asistencia - Servicio ZKTeco"

# Configurar descripción del servicio
sc.exe description "AttendanceSystem.ZKTeco.Service" "Servicio gRPC para comunicación con relojes checadores ZKTeco"
```

**Parámetros explicados**:
- `binPath=` - Ruta completa al ejecutable (sin comillas en la ruta si no tiene espacios)
- `start= auto` - Inicio automático con Windows
- `DisplayName=` - Nombre que aparece en Servicios de Windows

## Paso 7: Configurar Variables de Entorno (Importante)

El servicio debe usar la configuración de producción:

```powershell
# Establecer variable de entorno para el servicio
$serviceName = "AttendanceSystem.ZKTeco.Service"

# Crear clave de registro para variables de entorno del servicio
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"

# Agregar variable de entorno ASPNETCORE_ENVIRONMENT
New-ItemProperty -Path $regPath -Name "Environment" -Value "ASPNETCORE_ENVIRONMENT=Production" -PropertyType MultiString -Force
```

## Paso 8: Iniciar el Servicio

```powershell
# Iniciar el servicio
sc.exe start "AttendanceSystem.ZKTeco.Service"

# O usando PowerShell
Start-Service -Name "AttendanceSystem.ZKTeco.Service"
```

## Paso 9: Verificar que Funciona

### Verificar Estado del Servicio

```powershell
# Ver estado
Get-Service "AttendanceSystem.ZKTeco.Service"

# Ver detalles completos
Get-Service "AttendanceSystem.ZKTeco.Service" | Select-Object *
```

### Verificar que Está Escuchando en el Puerto

```powershell
# Verificar puerto 5001
netstat -ano | findstr :5001

# O usando PowerShell
Get-NetTCPConnection -LocalPort 5001 -State Listen
```

Deberías ver algo como:
```
TCP    0.0.0.0:5001           0.0.0.0:0              LISTENING       1234
```

### Verificar Logs del Servicio

```powershell
# Ver eventos del servicio en Event Viewer
Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco.Service" -Newest 10

# O abrir Event Viewer
eventvwr.msc
```

## Paso 10: Probar Conectividad desde la Aplicación Blazor

1. Accede a la aplicación web
2. Ve a la sección de "Dispositivos"
3. Intenta conectar a un dispositivo
4. Verifica que la conexión sea exitosa

## Troubleshooting

### El servicio no inicia

**Error**: "El servicio no respondió a tiempo"

**Soluciones**:

1. **Verificar permisos**:
   ```powershell
   # Dar permisos de ejecución
   icacls "C:\Services\AttendanceSystem.ZKTeco.Service" /grant "NT AUTHORITY\NETWORK SERVICE:(OI)(CI)F" /T
   ```

2. **Verificar dependencias**:
   ```powershell
   # Verificar que .NET Runtime está instalado
   dotnet --list-runtimes
   ```

3. **Ver logs detallados**:
   ```powershell
   # Ejecutar manualmente para ver errores
   cd "C:\Services\AttendanceSystem.ZKTeco.Service"
   .\AttendanceSystem.ZKTeco.Service.exe
   ```

### El puerto 5001 ya está en uso

```powershell
# Ver qué proceso está usando el puerto
Get-Process -Id (Get-NetTCPConnection -LocalPort 5001).OwningProcess

# Detener el proceso si es necesario
Stop-Process -Id XXXX -Force
```

### Error de arquitectura x86/x64

**Error**: "BadImageFormatException"

**Causa**: El servicio debe compilarse para x86 (32-bit) para usar el SDK de ZKTeco.

**Solución**:
```powershell
# Recompilar con arquitectura correcta
dotnet publish -r win-x86 --self-contained true
```

## Script Completo de Reinstalación

Aquí está todo en un solo script para facilitar:

```powershell
# ============================================
# Script de Reinstalación del Servicio ZKTeco
# ============================================

$serviceName = "AttendanceSystem.ZKTeco.Service"
$serviceDisplayName = "Sistema de Asistencia - Servicio ZKTeco"
$servicePath = "C:\Services\AttendanceSystem.ZKTeco.Service"
$exePath = "$servicePath\AttendanceSystem.ZKTeco.Service.exe"

Write-Host "=== Reinstalación del Servicio ZKTeco ===" -ForegroundColor Cyan
Write-Host ""

# 1. Detener servicio si existe
Write-Host "1. Deteniendo servicio..." -ForegroundColor Yellow
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Stop-Service -Name $serviceName -Force
        Write-Host "   ✓ Servicio detenido" -ForegroundColor Green
    } else {
        Write-Host "   ℹ Servicio ya estaba detenido" -ForegroundColor Gray
    }
} else {
    Write-Host "   ℹ Servicio no existe" -ForegroundColor Gray
}

# 2. Eliminar servicio
Write-Host "2. Eliminando servicio..." -ForegroundColor Yellow
if ($service) {
    sc.exe delete $serviceName
    Start-Sleep -Seconds 2
    Write-Host "   ✓ Servicio eliminado" -ForegroundColor Green
} else {
    Write-Host "   ℹ No hay servicio que eliminar" -ForegroundColor Gray
}

# 3. Verificar que el ejecutable existe
Write-Host "3. Verificando archivos..." -ForegroundColor Yellow
if (Test-Path $exePath) {
    Write-Host "   ✓ Ejecutable encontrado: $exePath" -ForegroundColor Green
} else {
    Write-Host "   ✗ ERROR: No se encuentra el ejecutable en $exePath" -ForegroundColor Red
    Write-Host "   Por favor, copia los archivos del servicio primero" -ForegroundColor Yellow
    exit 1
}

# 4. Crear servicio
Write-Host "4. Creando servicio..." -ForegroundColor Yellow
sc.exe create $serviceName binPath= $exePath start= auto DisplayName= $serviceDisplayName
sc.exe description $serviceName "Servicio gRPC para comunicación con relojes checadores ZKTeco"
Write-Host "   ✓ Servicio creado" -ForegroundColor Green

# 5. Configurar variable de entorno
Write-Host "5. Configurando entorno de producción..." -ForegroundColor Yellow
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
New-ItemProperty -Path $regPath -Name "Environment" -Value "ASPNETCORE_ENVIRONMENT=Production" -PropertyType MultiString -Force | Out-Null
Write-Host "   ✓ Variable de entorno configurada" -ForegroundColor Green

# 6. Iniciar servicio
Write-Host "6. Iniciando servicio..." -ForegroundColor Yellow
Start-Service -Name $serviceName
Start-Sleep -Seconds 3

# 7. Verificar
Write-Host "7. Verificando..." -ForegroundColor Yellow
$service = Get-Service -Name $serviceName
if ($service.Status -eq "Running") {
    Write-Host "   ✓ Servicio ejecutándose correctamente" -ForegroundColor Green
    
    # Verificar puerto
    $listening = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
    if ($listening) {
        Write-Host "   ✓ Puerto 5001 escuchando" -ForegroundColor Green
    } else {
        Write-Host "   ⚠ Puerto 5001 NO está escuchando" -ForegroundColor Yellow
        Write-Host "   Revisar logs del servicio" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ✗ ERROR: El servicio no está ejecutándose" -ForegroundColor Red
    Write-Host "   Estado: $($service.Status)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Reinstalación Completada ===" -ForegroundColor Cyan
```

## Guardar y Ejecutar el Script

1. **Guarda el script**:
   ```powershell
   # Guardar como Reinstall-ZKTecoService.ps1
   ```

2. **Ejecuta con privilegios de administrador**:
   ```powershell
   # Abrir PowerShell como Administrador
   Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
   .\Reinstall-ZKTecoService.ps1
   ```

## Notas Finales

- ✅ El servicio ahora usa la configuración corregida (sin IPs hardcodeadas)
- ✅ Los dispositivos se gestionan desde la base de datos
- ✅ El protocolo gRPC usa HTTP correctamente
- ✅ El servicio inicia automáticamente con Windows

Si tienes problemas, ejecuta el script de diagnóstico:
```powershell
.\Docs\Diagnose-AttendanceSystem.ps1
```
