# Guía de Despliegue en IIS - AttendanceSystem

## 📋 Requisitos Previos

### 1. Servidor Windows

#### Software Base
- **Windows Server 2016/2019/2022** o **Windows 10/11 Pro**
- **IIS (Internet Information Services)** versión 10.0 o superior
- **ASP.NET Core Hosting Bundle** (versión 8.0 o la que use tu aplicación)
  - Descarga: https://dotnet.microsoft.com/download/dotnet/8.0
  - Incluye:
    - .NET Runtime
    - ASP.NET Core Runtime
    - ASP.NET Core Module V2 (ANCM)

#### Características de Windows a Habilitar

**Vía GUI (Panel de Control):**
1. Panel de Control → Programas → Activar o desactivar características de Windows
2. Habilitar:
   - ✅ Internet Information Services
   - ✅ IIS → Servicios World Wide Web → Características de desarrollo de aplicaciones
     - ASP.NET 4.8
     - Extensibilidad de .NET 4.8
     - Extensiones ISAPI
     - Filtros ISAPI
   - ✅ IIS → Servicios World Wide Web → Características HTTP comunes
     - Documento predeterminado
     - Examen de directorios
     - Errores HTTP
     - Contenido estático
   - ✅ IIS → Herramientas de administración web
     - Consola de administración de IIS

**Vía PowerShell (Administrador):**
```powershell
# Instalar IIS con características necesarias
Install-WindowsFeature -name Web-Server -IncludeManagementTools
Install-WindowsFeature -name Web-Asp-Net45
Install-WindowsFeature -name Web-WebSockets
```

### 2. Base de Datos PostgreSQL

#### En el Servidor de Base de Datos
- **PostgreSQL 12+** instalado y en ejecución
- **Puerto 5432** abierto en el firewall (o el puerto personalizado que uses)
- **Usuario de aplicación** creado con permisos apropiados
- **Base de datos** `AttendanceSystem` creada

#### Configuración de Firewall PostgreSQL
```powershell
# Abrir puerto PostgreSQL en Windows Firewall
New-NetFirewallRule -DisplayName "PostgreSQL" -Direction Inbound -LocalPort 5432 -Protocol TCP -Action Allow
```

#### Verificar Conexión
```powershell
# Desde el servidor IIS, probar conexión a PostgreSQL
Test-NetConnection -ComputerName <IP_SERVIDOR_POSTGRES> -Port 5432
```

### 3. Configuración de PostgreSQL

Editar `postgresql.conf`:
```conf
listen_addresses = '*'  # O especificar IPs permitidas
max_connections = 100
```

Editar `pg_hba.conf` (agregar línea para permitir conexiones desde el servidor IIS):
```conf
# TYPE  DATABASE        USER            ADDRESS                 METHOD
host    AttendanceSystem app_user        <IP_SERVIDOR_IIS>/32    md5
```

Reiniciar PostgreSQL después de cambios.

---

## 🔧 Preparación de la Aplicación

### 1. Configuración de Producción

#### Crear `appsettings.Production.json`
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=<IP_SERVIDOR_POSTGRES>;Port=5432;Database=AttendanceSystem;Username=app_user;Password=<PASSWORD_SEGURO>;SSL Mode=Prefer;Trust Server Certificate=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ZKTeco": {
    "DeviceIP": "<IP_DISPOSITIVO_ZKTECO>",
    "DevicePort": 4370,
    "SyncIntervalMinutes": 30
  }
}
```

⚠️ **IMPORTANTE**: Nunca incluir `appsettings.Production.json` en el control de versiones. Agregarlo a `.gitignore`.

### 2. Publicación de la Aplicación

#### Opción A: Visual Studio
1. Click derecho en `AttendanceSystem.Blazor.Server` → **Publicar**
2. Seleccionar **Carpeta**
3. Ruta: `C:\Publish\AttendanceSystem`
4. Configuración:
   - **Configuration**: Release
   - **Target Framework**: net8.0 (o tu versión)
   - **Deployment Mode**: Self-contained o Framework-dependent
   - **Target Runtime**: win-x64

#### Opción B: Línea de Comandos
```powershell
# Navegar a la carpeta del proyecto
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.Blazor.Server"

# Publicar la aplicación
dotnet publish -c Release -o "C:\Publish\AttendanceSystem" --runtime win-x64 --self-contained false

# Si prefieres self-contained (incluye el runtime)
dotnet publish -c Release -o "C:\Publish\AttendanceSystem" --runtime win-x64 --self-contained true
```

### 3. Aplicar Migraciones de Base de Datos

**Antes de desplegar**, asegúrate de que la base de datos esté actualizada:

```powershell
# Desde la carpeta del proyecto Infrastructure
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Infrastructure\AttendanceSystem.Infrastructure"

# Aplicar migraciones (usando connection string de producción)
$env:ASPNETCORE_ENVIRONMENT="Production"
dotnet ef database update --startup-project "..\..\Presentation\AttendanceSystem.Blazor.Server"
```

O ejecutar desde la aplicación publicada:
```powershell
cd "C:\Publish\AttendanceSystem"
.\AttendanceSystem.Blazor.Server.exe --migrate
```

---

## 🌐 Configuración de IIS

### 1. Crear Application Pool

1. Abrir **IIS Manager** (inetmgr)
2. Click derecho en **Application Pools** → **Add Application Pool**
   - **Name**: `AttendanceSystemPool`
   - **.NET CLR version**: **No Managed Code** (importante para .NET Core)
   - **Managed pipeline mode**: Integrated
3. Click derecho en `AttendanceSystemPool` → **Advanced Settings**:
   - **Identity**: ApplicationPoolIdentity (o cuenta de servicio específica)
   - **Start Mode**: AlwaysRunning (opcional, para mejor rendimiento)
   - **Idle Time-out (minutes)**: 0 (para que no se detenga)
   - **Regular Time Interval (minutes)**: 0 (desactivar reciclaje automático)

### 2. Crear Sitio Web

1. Click derecho en **Sites** → **Add Website**
   - **Site name**: AttendanceSystem
   - **Application pool**: AttendanceSystemPool
   - **Physical path**: `C:\Publish\AttendanceSystem`
   - **Binding**:
     - Type: http
     - IP address: All Unassigned
     - Port: 80 (o el puerto que prefieras)
     - Host name: (opcional, ej: attendance.tuempresa.com)

2. Si usas HTTPS (recomendado):
   - Agregar binding HTTPS en puerto 443
   - Seleccionar certificado SSL

### 3. Permisos de Carpeta

El Application Pool necesita permisos en la carpeta de publicación:

```powershell
# Dar permisos de lectura/ejecución al Application Pool
$path = "C:\Publish\AttendanceSystem"
$acl = Get-Acl $path
$permission = "IIS AppPool\AttendanceSystemPool","ReadAndExecute","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $path $acl
```

### 4. Configuración de web.config

IIS debería generar automáticamente un `web.config`. Verificar que contenga:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\AttendanceSystem.Blazor.Server.dll" 
                  stdoutLogEnabled="true" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

### 5. Crear Carpeta de Logs

```powershell
New-Item -Path "C:\Publish\AttendanceSystem\logs" -ItemType Directory -Force

# Dar permisos de escritura
$path = "C:\Publish\AttendanceSystem\logs"
$acl = Get-Acl $path
$permission = "IIS AppPool\AttendanceSystemPool","Modify","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $path $acl
```

---

## 🔒 Seguridad

### 1. HTTPS/SSL

**Obtener Certificado SSL:**
- **Let's Encrypt** (gratuito): Usar win-acme
- **Certificado comercial**: Comprar de CA reconocida
- **Certificado autofirmado** (solo desarrollo/interno)

**Configurar HTTPS en IIS:**
1. Importar certificado en el servidor
2. Agregar binding HTTPS en el sitio
3. Forzar redirección HTTPS en `Startup.cs` o `Program.cs`

### 2. Firewall

```powershell
# Abrir puerto HTTP
New-NetFirewallRule -DisplayName "AttendanceSystem HTTP" -Direction Inbound -LocalPort 80 -Protocol TCP -Action Allow

# Abrir puerto HTTPS
New-NetFirewallRule -DisplayName "AttendanceSystem HTTPS" -Direction Inbound -LocalPort 443 -Protocol TCP -Action Allow
```

### 3. Seguridad de Connection String

**Opción 1: Variables de Entorno**
```powershell
[System.Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Host=...', 'Machine')
```

**Opción 2: Azure Key Vault / Secrets Manager** (para producción empresarial)

---

## 🔄 Servicio de Windows (ZKTeco.Service)

Si tienes un servicio de Windows para sincronización con ZKTeco:

### 1. Publicar el Servicio
```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.ZKTeco.Service"
dotnet publish -c Release -o "C:\Services\AttendanceSystem.ZKTeco.Service" --runtime win-x64
```

### 2. Instalar como Servicio de Windows
```powershell
# Usando sc.exe
sc.exe create "AttendanceSystem.ZKTeco" binPath="C:\Services\AttendanceSystem.ZKTeco.Service\AttendanceSystem.ZKTeco.Service.exe" start=auto

# O usando NSSM (recomendado)
# Descargar NSSM de https://nssm.cc/download
nssm install AttendanceSystem.ZKTeco "C:\Services\AttendanceSystem.ZKTeco.Service\AttendanceSystem.ZKTeco.Service.exe"
nssm set AttendanceSystem.ZKTeco AppDirectory "C:\Services\AttendanceSystem.ZKTeco.Service"
nssm set AttendanceSystem.ZKTeco Start SERVICE_AUTO_START

# Iniciar el servicio
Start-Service AttendanceSystem.ZKTeco
```

---

## ✅ Verificación Post-Despliegue

### 1. Verificar que el Sitio Funciona
```powershell
# Probar localmente en el servidor
Invoke-WebRequest -Uri "http://localhost" -UseBasicParsing

# Probar desde otra máquina
Invoke-WebRequest -Uri "http://<IP_SERVIDOR>" -UseBasicParsing
```

### 2. Revisar Logs
- **IIS Logs**: `C:\inetpub\logs\LogFiles\`
- **Application Logs**: `C:\Publish\AttendanceSystem\logs\`
- **Event Viewer**: Windows Logs → Application

### 3. Verificar Conexión a Base de Datos
- Acceder a la aplicación
- Intentar login
- Verificar que se muestren datos

### 4. Verificar Servicio ZKTeco (si aplica)
```powershell
Get-Service AttendanceSystem.ZKTeco
Get-EventLog -LogName Application -Source AttendanceSystem.ZKTeco -Newest 10
```

---

## 🔄 Proceso de Actualización

### Script de Actualización
```powershell
# stop_and_update.ps1

# 1. Detener el sitio
Stop-WebSite -Name "AttendanceSystem"
Stop-WebAppPool -Name "AttendanceSystemPool"

# 2. Esperar a que se detenga completamente
Start-Sleep -Seconds 5

# 3. Backup de la versión actual
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "C:\Publish\AttendanceSystem" -Destination "C:\Backups\AttendanceSystem_$timestamp" -Recurse

# 4. Copiar nueva versión (desde carpeta de publicación temporal)
Copy-Item -Path "C:\Temp\NewPublish\*" -Destination "C:\Publish\AttendanceSystem" -Recurse -Force

# 5. Aplicar migraciones (si es necesario)
cd "C:\Publish\AttendanceSystem"
# Ejecutar comando de migración si tu app lo soporta

# 6. Reiniciar el sitio
Start-WebAppPool -Name "AttendanceSystemPool"
Start-WebSite -Name "AttendanceSystem"

Write-Host "Actualización completada" -ForegroundColor Green
```

---

## 📊 Monitoreo y Mantenimiento

### 1. Health Checks
Configurar endpoint de health check en la aplicación y monitorearlo.

### 2. Logs Centralizados
Considerar integrar con:
- **Seq** (para logs estructurados)
- **Application Insights** (Azure)
- **ELK Stack** (Elasticsearch, Logstash, Kibana)

### 3. Backups Automáticos
```powershell
# Backup de base de datos PostgreSQL
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
& "C:\Program Files\PostgreSQL\15\bin\pg_dump.exe" -h localhost -U app_user -d AttendanceSystem -F c -f "C:\Backups\DB\AttendanceSystem_$timestamp.backup"
```

---

## 🆘 Troubleshooting

### Error 500.19 - Internal Server Error
- **Causa**: ASP.NET Core Module no instalado
- **Solución**: Instalar ASP.NET Core Hosting Bundle

### Error 502.5 - Process Failure
- **Causa**: Aplicación no puede iniciarse
- **Solución**: Revisar logs en `C:\Publish\AttendanceSystem\logs\stdout`

### Error de Conexión a Base de Datos
- **Verificar**: Connection string en `appsettings.Production.json`
- **Verificar**: Firewall permite conexión al puerto PostgreSQL
- **Verificar**: Usuario y contraseña correctos en PostgreSQL

### Aplicación Lenta
- **Verificar**: Application Pool no está reciclándose constantemente
- **Verificar**: Recursos del servidor (CPU, RAM, Disco)
- **Verificar**: Índices en base de datos PostgreSQL

---

## 📝 Checklist de Despliegue

- [ ] Windows Server configurado con IIS
- [ ] ASP.NET Core Hosting Bundle instalado
- [ ] PostgreSQL instalado y configurado
- [ ] Base de datos creada y migraciones aplicadas
- [ ] Aplicación publicada en modo Release
- [ ] `appsettings.Production.json` configurado correctamente
- [ ] Application Pool creado con "No Managed Code"
- [ ] Sitio web creado en IIS
- [ ] Permisos de carpeta configurados
- [ ] Firewall configurado (puertos 80, 443, 5432)
- [ ] SSL/HTTPS configurado (producción)
- [ ] Servicio ZKTeco instalado (si aplica)
- [ ] Logs funcionando correctamente
- [ ] Backup strategy implementada
- [ ] Pruebas de funcionalidad completadas

---

## 📚 Referencias

- [Deploy ASP.NET Core apps to IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/)
- [ASP.NET Core Module (ANCM)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/aspnet-core-module)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [IIS Configuration Reference](https://learn.microsoft.com/en-us/iis/configuration/)
