# Guía de Despliegue - AttendanceSystem.ZKTeco.Service

## 📋 Descripción

**AttendanceSystem.ZKTeco.Service** es un servicio de Windows que actúa como intermediario entre los dispositivos biométricos ZKTeco y la aplicación web Blazor. Este servicio:

- ✅ Se ejecuta como **Servicio de Windows** en segundo plano
- ✅ Expone un **servidor gRPC** para comunicación con la aplicación web
- ✅ Gestiona la comunicación con dispositivos ZKTeco mediante el SDK x86
- ✅ Sincroniza datos de asistencia automáticamente
- ✅ Se inicia automáticamente con el sistema

## 🏗️ Arquitectura

```
┌─────────────────────────────────────────────────────────────┐
│                    Aplicación Blazor (IIS)                   │
│                    (Puerto 80/443)                            │
└───────────────────────────┬─────────────────────────────────┘
                            │ gRPC
                            │ (Puerto 5001)
                            ▼
┌─────────────────────────────────────────────────────────────┐
│         AttendanceSystem.ZKTeco.Service                       │
│         (Servicio de Windows)                                 │
│         - Servidor gRPC                                       │
│         - SDK ZKTeco x86                                      │
└───────────────────────────┬─────────────────────────────────┘
                            │ TCP/IP
                            │ (Puerto 4370)
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              Dispositivos Biométricos ZKTeco                  │
│              (Red local)                                      │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Requisitos

### Software
- **Windows Server 2016/2019/2022** o **Windows 10/11**
- **.NET 9.0 Runtime** (x86 - 32 bits) ⚠️ **IMPORTANTE: Debe ser x86**
  - Descarga: https://dotnet.microsoft.com/download/dotnet/9.0
- **Visual C++ Redistributable** (x86)
  - Descarga: https://aka.ms/vs/17/release/vc_redist.x86.exe

### Hardware
- **CPU**: 2 cores o más
- **RAM**: 2 GB mínimo
- **Red**: Conectividad con dispositivos ZKTeco

### Red
- **Puerto 5001**: gRPC Server (comunicación con aplicación Blazor)
- **Puerto 4370**: Comunicación con dispositivos ZKTeco
- **Firewall**: Configurado para permitir comunicación bidireccional

## 🚀 Instalación

### Método 1: Script Automatizado (Recomendado)

Usa el script `install-zkteco-service.ps1` que se proporciona:

```powershell
# Ejecutar como Administrador
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\install-zkteco-service.ps1
```

### Método 2: Instalación Manual

#### Paso 1: Publicar el Servicio

```powershell
# Navegar al proyecto del servicio
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.ZKTeco.Service"

# Publicar (IMPORTANTE: runtime win-x86 porque el SDK ZKTeco es 32 bits)
dotnet publish -c Release -o "C:\Services\AttendanceSystem.ZKTeco" --runtime win-x86 --self-contained true

# Verificar que se crearon los archivos
dir "C:\Services\AttendanceSystem.ZKTeco"
```

⚠️ **CRÍTICO**: Debe ser `win-x86` (32 bits) porque el SDK de ZKTeco solo funciona en x86.

#### Paso 2: Configurar appsettings.json

Crear o editar `C:\Services\AttendanceSystem.ZKTeco\appsettings.json`:

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
  "ZKTeco": {
    "DeviceIP": "192.168.1.100",
    "DevicePort": 4370,
    "ConnectionTimeout": 30,
    "ReadTimeout": 30,
    "AutoReconnect": true,
    "ReconnectIntervalSeconds": 60
  },
  "AllowedHosts": "*"
}
```

#### Paso 3: Instalar como Servicio de Windows

**Opción A: Usando sc.exe (Nativo de Windows)**

```powershell
# Crear el servicio
sc.exe create "AttendanceSystem.ZKTeco" `
    binPath= "C:\Services\AttendanceSystem.ZKTeco\AttendanceSystem.ZKTeco.Service.exe" `
    start= auto `
    DisplayName= "AttendanceSystem ZKTeco Service" `
    description= "Servicio de sincronización con dispositivos biométricos ZKTeco"

# Configurar recuperación automática en caso de fallo
sc.exe failure "AttendanceSystem.ZKTeco" reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Iniciar el servicio
sc.exe start "AttendanceSystem.ZKTeco"
```

**Opción B: Usando NSSM (Recomendado - Más Control)**

NSSM (Non-Sucking Service Manager) proporciona mejor control sobre el servicio.

```powershell
# Descargar NSSM desde https://nssm.cc/download
# Extraer a C:\Tools\nssm

# Instalar el servicio
C:\Tools\nssm\win64\nssm.exe install AttendanceSystem.ZKTeco "C:\Services\AttendanceSystem.ZKTeco\AttendanceSystem.ZKTeco.Service.exe"

# Configurar el servicio
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppDirectory "C:\Services\AttendanceSystem.ZKTeco"
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco DisplayName "AttendanceSystem ZKTeco Service"
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco Description "Servicio de sincronización con dispositivos biométricos ZKTeco"
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco Start SERVICE_AUTO_START

# Configurar logs
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppStdout "C:\Services\AttendanceSystem.ZKTeco\logs\service-output.log"
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppStderr "C:\Services\AttendanceSystem.ZKTeco\logs\service-error.log"

# Configurar rotación de logs (10 MB)
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppRotateFiles 1
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppRotateBytes 10485760

# Iniciar el servicio
C:\Tools\nssm\win64\nssm.exe start AttendanceSystem.ZKTeco
```

#### Paso 4: Configurar Firewall

```powershell
# Permitir puerto gRPC (5001)
New-NetFirewallRule -DisplayName "AttendanceSystem ZKTeco gRPC" `
    -Direction Inbound `
    -LocalPort 5001 `
    -Protocol TCP `
    -Action Allow

# Permitir comunicación con dispositivos ZKTeco (4370)
New-NetFirewallRule -DisplayName "ZKTeco Devices" `
    -Direction Inbound `
    -LocalPort 4370 `
    -Protocol TCP `
    -Action Allow

New-NetFirewallRule -DisplayName "ZKTeco Devices Outbound" `
    -Direction Outbound `
    -RemotePort 4370 `
    -Protocol TCP `
    -Action Allow
```

#### Paso 5: Crear Carpeta de Logs

```powershell
# Crear carpeta de logs
New-Item -Path "C:\Services\AttendanceSystem.ZKTeco\logs" -ItemType Directory -Force

# Dar permisos al servicio
$acl = Get-Acl "C:\Services\AttendanceSystem.ZKTeco\logs"
$permission = "NT AUTHORITY\LOCAL SERVICE","Modify","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl "C:\Services\AttendanceSystem.ZKTeco\logs" $acl
```

## ✅ Verificación

### 1. Verificar que el Servicio Está Corriendo

```powershell
# Ver estado del servicio
Get-Service -Name "AttendanceSystem.ZKTeco"

# Ver detalles
Get-Service -Name "AttendanceSystem.ZKTeco" | Select-Object *

# Ver en Services.msc
services.msc
```

### 2. Verificar que el Puerto gRPC Está Escuchando

```powershell
# Verificar que el puerto 5001 está en uso
Get-NetTCPConnection -LocalPort 5001 -State Listen

# O usando netstat
netstat -ano | findstr :5001
```

### 3. Probar Conexión gRPC

```powershell
# Probar que el puerto responde
Test-NetConnection -ComputerName localhost -Port 5001
```

### 4. Revisar Logs

```powershell
# Ver logs del servicio (si usas NSSM)
Get-Content "C:\Services\AttendanceSystem.ZKTeco\logs\service-output.log" -Tail 50 -Wait

# Ver logs de la aplicación
Get-Content "C:\Services\AttendanceSystem.ZKTeco\logs\*.log" -Tail 50

# Ver eventos del sistema
Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco" -Newest 20
```

### 5. Probar Conexión con Dispositivo ZKTeco

Desde la aplicación Blazor, intenta conectarte al dispositivo. Si hay problemas:

```powershell
# Ping al dispositivo
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370

# Verificar que el dispositivo está en la red
ping 192.168.1.100
```

## 🔧 Configuración Avanzada

### Configurar Múltiples Dispositivos

Editar `appsettings.json`:

```json
{
  "ZKTeco": {
    "Devices": [
      {
        "Name": "Entrada Principal",
        "IP": "192.168.1.100",
        "Port": 4370
      },
      {
        "Name": "Salida Trasera",
        "IP": "192.168.1.101",
        "Port": 4370
      }
    ],
    "ConnectionTimeout": 30,
    "AutoReconnect": true
  }
}
```

### Configurar Logging Avanzado

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Grpc": "Debug",
      "AttendanceSystem.ZKTeco": "Debug"
    },
    "File": {
      "Path": "logs/zkteco-.log",
      "RollingInterval": "Day",
      "RetainedFileCountLimit": 30,
      "FileSizeLimitBytes": 10485760
    }
  }
}
```

### Configurar Usuario del Servicio

Por defecto, el servicio se ejecuta como `LOCAL SERVICE`. Para usar una cuenta específica:

```powershell
# Usando sc.exe
sc.exe config "AttendanceSystem.ZKTeco" obj= "DOMINIO\Usuario" password= "Password"

# Usando NSSM
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco ObjectName "DOMINIO\Usuario" "Password"

# Reiniciar el servicio
Restart-Service -Name "AttendanceSystem.ZKTeco"
```

## 🔄 Actualización del Servicio

### Usando Script

```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\update-zkteco-service.ps1 -NewVersionPath "C:\Temp\NewServicePublish"
```

### Manual

```powershell
# 1. Detener el servicio
Stop-Service -Name "AttendanceSystem.ZKTeco"

# 2. Crear backup
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "C:\Services\AttendanceSystem.ZKTeco" `
          -Destination "C:\Backups\ZKTecoService_$timestamp" `
          -Recurse

# 3. Publicar nueva versión
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.ZKTeco.Service"
dotnet publish -c Release -o "C:\Temp\NewServicePublish" --runtime win-x86 --self-contained true

# 4. Preservar configuración
Copy-Item "C:\Services\AttendanceSystem.ZKTeco\appsettings.json" `
          "C:\Temp\appsettings.json.bak"

# 5. Copiar nueva versión
Copy-Item -Path "C:\Temp\NewServicePublish\*" `
          -Destination "C:\Services\AttendanceSystem.ZKTeco" `
          -Recurse -Force

# 6. Restaurar configuración
Copy-Item "C:\Temp\appsettings.json.bak" `
          "C:\Services\AttendanceSystem.ZKTeco\appsettings.json" `
          -Force

# 7. Iniciar el servicio
Start-Service -Name "AttendanceSystem.ZKTeco"

# 8. Verificar
Get-Service -Name "AttendanceSystem.ZKTeco"
```

## 🗑️ Desinstalación

### Usando sc.exe

```powershell
# Detener el servicio
sc.exe stop "AttendanceSystem.ZKTeco"

# Eliminar el servicio
sc.exe delete "AttendanceSystem.ZKTeco"

# Eliminar archivos (opcional)
Remove-Item -Path "C:\Services\AttendanceSystem.ZKTeco" -Recurse -Force
```

### Usando NSSM

```powershell
# Detener y eliminar el servicio
C:\Tools\nssm\win64\nssm.exe stop AttendanceSystem.ZKTeco
C:\Tools\nssm\win64\nssm.exe remove AttendanceSystem.ZKTeco confirm

# Eliminar archivos (opcional)
Remove-Item -Path "C:\Services\AttendanceSystem.ZKTeco" -Recurse -Force
```

## 🆘 Troubleshooting

### El Servicio No Inicia

**Verificar logs**:
```powershell
# Event Viewer
Get-EventLog -LogName Application -Newest 20 | Where-Object { $_.Source -like "*AttendanceSystem*" }

# Logs del servicio
Get-Content "C:\Services\AttendanceSystem.ZKTeco\logs\service-error.log"
```

**Causas comunes**:
- ❌ .NET Runtime x86 no instalado
- ❌ Puerto 5001 ya en uso
- ❌ Permisos insuficientes
- ❌ Archivo de configuración inválido

**Soluciones**:
```powershell
# Verificar .NET Runtime x86
dotnet --list-runtimes

# Verificar puerto
Get-NetTCPConnection -LocalPort 5001

# Ejecutar manualmente para ver errores
cd "C:\Services\AttendanceSystem.ZKTeco"
.\AttendanceSystem.ZKTeco.Service.exe
```

### Error "BadImageFormatException"

**Causa**: Intentando ejecutar en x64 cuando el SDK ZKTeco es x86.

**Solución**: Re-publicar con `--runtime win-x86`:
```powershell
dotnet publish -c Release -o "C:\Services\AttendanceSystem.ZKTeco" --runtime win-x86 --self-contained true
```

### No Se Puede Conectar al Dispositivo ZKTeco

**Verificar**:
```powershell
# Ping al dispositivo
ping 192.168.1.100

# Verificar puerto
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370

# Verificar firewall
Get-NetFirewallRule -DisplayName "*ZKTeco*"
```

**Soluciones**:
- Verificar que el dispositivo está encendido
- Verificar configuración de red del dispositivo
- Verificar firewall de Windows
- Verificar que la IP es correcta en `appsettings.json`

### La Aplicación Blazor No Se Puede Conectar al Servicio

**Verificar**:
```powershell
# Servicio corriendo
Get-Service -Name "AttendanceSystem.ZKTeco"

# Puerto escuchando
Get-NetTCPConnection -LocalPort 5001 -State Listen

# Firewall
Get-NetFirewallRule -DisplayName "*AttendanceSystem*gRPC*"
```

**Probar conexión**:
```powershell
Test-NetConnection -ComputerName localhost -Port 5001
```

### El Servicio Se Detiene Inesperadamente

**Configurar recuperación automática**:
```powershell
# Con sc.exe
sc.exe failure "AttendanceSystem.ZKTeco" reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Con NSSM (ya configurado por defecto)
C:\Tools\nssm\win64\nssm.exe set AttendanceSystem.ZKTeco AppExit Default Restart
```

## 📊 Monitoreo

### Script de Monitoreo

```powershell
# monitor-zkteco-service.ps1
while ($true) {
    $service = Get-Service -Name "AttendanceSystem.ZKTeco"
    
    if ($service.Status -ne "Running") {
        Write-Host "⚠️ ALERTA: El servicio no está corriendo!" -ForegroundColor Red
        
        # Intentar reiniciar
        Start-Service -Name "AttendanceSystem.ZKTeco"
        
        # Enviar notificación (email, SMS, etc.)
        # Send-MailMessage ...
    } else {
        Write-Host "✓ Servicio OK - $(Get-Date)" -ForegroundColor Green
    }
    
    # Verificar puerto gRPC
    $port = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
    if ($null -eq $port) {
        Write-Host "⚠️ ALERTA: Puerto gRPC 5001 no está escuchando!" -ForegroundColor Red
    }
    
    Start-Sleep -Seconds 60
}
```

### Tarea Programada de Monitoreo

```powershell
# Crear tarea que ejecuta el script de monitoreo
$action = New-ScheduledTaskAction -Execute 'PowerShell.exe' `
    -Argument '-File "C:\Scripts\monitor-zkteco-service.ps1"'

$trigger = New-ScheduledTaskTrigger -AtStartup

Register-ScheduledTask -TaskName "Monitor ZKTeco Service" `
    -Action $action `
    -Trigger $trigger `
    -User "SYSTEM" `
    -RunLevel Highest
```

## 📝 Comandos Útiles

```powershell
# Ver estado del servicio
Get-Service -Name "AttendanceSystem.ZKTeco"

# Iniciar servicio
Start-Service -Name "AttendanceSystem.ZKTeco"

# Detener servicio
Stop-Service -Name "AttendanceSystem.ZKTeco"

# Reiniciar servicio
Restart-Service -Name "AttendanceSystem.ZKTeco"

# Ver logs en tiempo real
Get-Content "C:\Services\AttendanceSystem.ZKTeco\logs\*.log" -Tail 50 -Wait

# Ver eventos del sistema
Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco" -Newest 20

# Ver procesos
Get-Process | Where-Object { $_.ProcessName -like "*AttendanceSystem*" }

# Ver conexiones de red
Get-NetTCPConnection | Where-Object { $_.LocalPort -eq 5001 -or $_.RemotePort -eq 4370 }

# Probar conexión gRPC
Test-NetConnection -ComputerName localhost -Port 5001

# Probar conexión a dispositivo
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370
```

## 🔗 Integración con Aplicación Blazor

La aplicación Blazor se conecta al servicio mediante gRPC. Asegúrate de que en `appsettings.json` de la aplicación Blazor esté configurado:

```json
{
  "GrpcServices": {
    "ZKTecoService": "http://localhost:5001"
  }
}
```

Si el servicio está en otro servidor:

```json
{
  "GrpcServices": {
    "ZKTecoService": "http://192.168.1.50:5001"
  }
}
```

## 📚 Referencias

- [.NET Worker Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [Windows Services in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [gRPC in .NET](https://learn.microsoft.com/en-us/aspnet/core/grpc/)
- [NSSM Documentation](https://nssm.cc/)

---

**Última actualización**: 2026-01-26  
**Versión**: 1.0
