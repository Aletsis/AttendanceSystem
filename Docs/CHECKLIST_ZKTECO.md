# Checklist - Servicio ZKTeco

## Pre-Instalación

### Requisitos del Sistema
- [ ] Windows Server 2016/2019/2022 o Windows 10/11
- [ ] .NET 9.0 Runtime x86 (32 bits) instalado
- [ ] Visual C++ Redistributable x86 instalado
- [ ] Conectividad de red con dispositivos ZKTeco

### Verificar Requisitos
```powershell
# Verificar .NET Runtime x86
dotnet --list-runtimes | findstr x86

# Verificar conectividad con dispositivo
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370
```

## Instalación

### Opción 1: Script Automatizado (Recomendado)
```powershell
# Ejecutar como Administrador
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"

# Instalación básica
.\install-zkteco-service.ps1

# Instalación con NSSM (mejor control)
.\install-zkteco-service.ps1 -UseNSSM -NSSMPath "C:\Tools\nssm\win64\nssm.exe"

# Instalación personalizada
.\install-zkteco-service.ps1 `
    -ServicePath "C:\Services\AttendanceSystem.ZKTeco" `
    -GrpcPort 5001 `
    -DeviceIP "192.168.1.100" `
    -DevicePort 4370 `
    -UseNSSM
```

### Opción 2: Manual

#### 1. Publicar el Servicio
```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.ZKTeco.Service"

# IMPORTANTE: Debe ser win-x86 (32 bits)
dotnet publish -c Release -o "C:\Services\AttendanceSystem.ZKTeco" --runtime win-x86 --self-contained true
```

#### 2. Configurar appsettings.json
- [ ] Copiar plantilla: `appsettings.ZKTecoService.json.template`
- [ ] Configurar IP del dispositivo ZKTeco
- [ ] Configurar puerto gRPC (por defecto 5001)
- [ ] Ajustar configuración de logging

#### 3. Instalar como Servicio
```powershell
# Con sc.exe
sc.exe create "AttendanceSystem.ZKTeco" `
    binPath= "C:\Services\AttendanceSystem.ZKTeco\AttendanceSystem.ZKTeco.Service.exe" `
    start= auto `
    DisplayName= "AttendanceSystem ZKTeco Service"

# O con NSSM (recomendado)
C:\Tools\nssm\win64\nssm.exe install AttendanceSystem.ZKTeco "C:\Services\AttendanceSystem.ZKTeco\AttendanceSystem.ZKTeco.Service.exe"
```

#### 4. Configurar Firewall
```powershell
# Puerto gRPC
New-NetFirewallRule -DisplayName "AttendanceSystem ZKTeco gRPC" -Direction Inbound -LocalPort 5001 -Protocol TCP -Action Allow

# Puerto ZKTeco
New-NetFirewallRule -DisplayName "ZKTeco Devices" -Direction Inbound -LocalPort 4370 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "ZKTeco Devices Outbound" -Direction Outbound -RemotePort 4370 -Protocol TCP -Action Allow
```

#### 5. Iniciar Servicio
```powershell
Start-Service -Name "AttendanceSystem.ZKTeco"
```

## Verificación Post-Instalación

### 1. Verificar Servicio
```powershell
# Estado del servicio
Get-Service -Name "AttendanceSystem.ZKTeco"

# Debe mostrar: Status = Running
```
- [ ] Servicio en estado "Running"

### 2. Verificar Puerto gRPC
```powershell
# Verificar que el puerto está escuchando
Get-NetTCPConnection -LocalPort 5001 -State Listen

# Probar conexión
Test-NetConnection -ComputerName localhost -Port 5001
```
- [ ] Puerto 5001 escuchando
- [ ] Conexión exitosa

### 3. Verificar Logs
```powershell
# Ver logs del servicio
Get-Content "C:\Services\AttendanceSystem.ZKTeco\logs\*.log" -Tail 50

# Ver eventos del sistema
Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco" -Newest 10
```
- [ ] No hay errores críticos en los logs
- [ ] Servicio inició correctamente

### 4. Verificar Conectividad con Dispositivo
```powershell
# Ping al dispositivo
ping 192.168.1.100

# Probar puerto
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370
```
- [ ] Dispositivo responde a ping
- [ ] Puerto 4370 accesible

### 5. Probar desde Aplicación Blazor
- [ ] Aplicación Blazor puede conectarse al servicio gRPC
- [ ] Se pueden leer datos del dispositivo ZKTeco
- [ ] Sincronización funciona correctamente

## Configuración de Aplicación Blazor

En `appsettings.json` de la aplicación Blazor:

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

## Actualización

### Usando Script
```powershell
# 1. Publicar nueva versión
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.ZKTeco.Service"
dotnet publish -c Release -o "C:\Temp\NewServicePublish" --runtime win-x86 --self-contained true

# 2. Actualizar
cd "..\..\..\..\Docs"
.\update-zkteco-service.ps1 -NewVersionPath "C:\Temp\NewServicePublish"
```

### Manual
1. [ ] Detener servicio: `Stop-Service -Name "AttendanceSystem.ZKTeco"`
2. [ ] Crear backup de carpeta actual
3. [ ] Preservar `appsettings.json`
4. [ ] Copiar nuevos archivos
5. [ ] Restaurar `appsettings.json`
6. [ ] Iniciar servicio: `Start-Service -Name "AttendanceSystem.ZKTeco"`
7. [ ] Verificar que funciona correctamente

## Rollback

```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\rollback-zkteco-service.ps1
```

## Monitoreo

### Configurar Monitoreo Automático
```powershell
# Ejecutar script de monitoreo
.\monitor-zkteco-service.ps1 -CheckIntervalSeconds 60 -DeviceIP "192.168.1.100"

# Con alertas por email
.\monitor-zkteco-service.ps1 `
    -EmailTo "admin@empresa.com" `
    -EmailFrom "monitor@empresa.com" `
    -SmtpServer "smtp.empresa.com"
```

### Crear Tarea Programada para Monitoreo
```powershell
$action = New-ScheduledTaskAction -Execute 'PowerShell.exe' `
    -Argument '-File "C:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs\monitor-zkteco-service.ps1"'

$trigger = New-ScheduledTaskTrigger -AtStartup

Register-ScheduledTask -TaskName "Monitor ZKTeco Service" `
    -Action $action `
    -Trigger $trigger `
    -User "SYSTEM" `
    -RunLevel Highest
```

## Troubleshooting

### El Servicio No Inicia

**Verificar**:
```powershell
# Ejecutar manualmente para ver errores
cd "C:\Services\AttendanceSystem.ZKTeco"
.\AttendanceSystem.ZKTeco.Service.exe

# Ver logs
Get-Content "logs\*.log" -Tail 50

# Ver Event Viewer
Get-EventLog -LogName Application -Newest 20 | Where-Object { $_.Source -like "*AttendanceSystem*" }
```

**Causas comunes**:
- [ ] .NET Runtime x86 no instalado
- [ ] Puerto 5001 ya en uso por otra aplicación
- [ ] Archivo `appsettings.json` inválido
- [ ] Permisos insuficientes

### Error "BadImageFormatException"

**Causa**: Ejecutando en x64 cuando debe ser x86

**Solución**:
```powershell
# Re-publicar con runtime correcto
dotnet publish -c Release -o "C:\Services\AttendanceSystem.ZKTeco" --runtime win-x86 --self-contained true
```

### Puerto gRPC No Escucha

**Verificar**:
```powershell
# Ver qué está usando el puerto
Get-NetTCPConnection -LocalPort 5001

# Ver procesos
Get-Process | Where-Object { $_.ProcessName -like "*AttendanceSystem*" }
```

**Soluciones**:
- Cambiar puerto en `appsettings.json`
- Detener aplicación que usa el puerto
- Verificar firewall

### No Conecta con Dispositivo ZKTeco

**Verificar**:
```powershell
# Conectividad básica
ping 192.168.1.100

# Puerto específico
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370

# Firewall
Get-NetFirewallRule -DisplayName "*ZKTeco*"
```

**Soluciones**:
- [ ] Verificar IP del dispositivo en `appsettings.json`
- [ ] Verificar que el dispositivo está encendido
- [ ] Verificar configuración de red del dispositivo
- [ ] Verificar firewall de Windows
- [ ] Verificar que el SDK ZKTeco está correctamente incluido

### Aplicación Blazor No Se Conecta al Servicio

**Verificar**:
```powershell
# Servicio corriendo
Get-Service -Name "AttendanceSystem.ZKTeco"

# Puerto escuchando
Get-NetTCPConnection -LocalPort 5001 -State Listen

# Desde servidor de aplicación Blazor
Test-NetConnection -ComputerName <IP_SERVIDOR_SERVICIO> -Port 5001
```

**Soluciones**:
- [ ] Verificar URL en `appsettings.json` de Blazor
- [ ] Verificar firewall entre servidores
- [ ] Verificar que el servicio está corriendo
- [ ] Revisar logs del servicio

## Comandos Útiles

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

# Ver configuración del servicio
sc.exe qc "AttendanceSystem.ZKTeco"

# Ver estado detallado
sc.exe query "AttendanceSystem.ZKTeco"
```

## Desinstalación

### Con sc.exe
```powershell
# Detener servicio
sc.exe stop "AttendanceSystem.ZKTeco"

# Eliminar servicio
sc.exe delete "AttendanceSystem.ZKTeco"

# Eliminar archivos (opcional)
Remove-Item -Path "C:\Services\AttendanceSystem.ZKTeco" -Recurse -Force
```

### Con NSSM
```powershell
# Detener y eliminar
C:\Tools\nssm\win64\nssm.exe stop AttendanceSystem.ZKTeco
C:\Tools\nssm\win64\nssm.exe remove AttendanceSystem.ZKTeco confirm

# Eliminar archivos (opcional)
Remove-Item -Path "C:\Services\AttendanceSystem.ZKTeco" -Recurse -Force
```

## Checklist de Seguridad

- [ ] Servicio ejecutándose con cuenta de servicio (no Administrator)
- [ ] Firewall configurado para permitir solo puertos necesarios
- [ ] Comunicación gRPC protegida (considerar TLS en producción)
- [ ] Logs no contienen información sensible
- [ ] Backups automáticos configurados
- [ ] Monitoreo activo configurado
- [ ] Alertas configuradas para fallos

## Notas Importantes

⚠️ **CRÍTICO**:
- El servicio DEBE compilarse para **win-x86 (32 bits)** porque el SDK de ZKTeco es de 32 bits
- No usar `--self-contained false` si el servidor no tiene .NET Runtime x86 instalado
- El puerto gRPC (5001) debe ser accesible desde el servidor donde corre la aplicación Blazor
- Siempre crear backups antes de actualizar

## Contactos de Emergencia

- **Administrador del Servidor**: _________________
- **Responsable de Dispositivos ZKTeco**: _________________
- **Desarrollador Principal**: _________________
- **Soporte Técnico**: _________________

---

**Última actualización**: 2026-01-26  
**Versión**: 1.0
