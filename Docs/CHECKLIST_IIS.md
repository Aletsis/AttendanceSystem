# Checklist Rápido - Despliegue IIS

## Pre-Despliegue

### En el Servidor
- [ ] Windows Server instalado y actualizado
- [ ] IIS instalado y configurado
- [ ] ASP.NET Core Hosting Bundle instalado (https://dotnet.microsoft.com/download/dotnet/8.0)
- [ ] PostgreSQL instalado y en ejecución
- [ ] Firewall configurado (puertos 80, 443, 5432)

### Base de Datos
- [ ] Base de datos `AttendanceSystem` creada
- [ ] Usuario de aplicación creado con permisos
- [ ] Connection string probado y funcionando
- [ ] Migraciones aplicadas

### Aplicación
- [ ] Código compilado sin errores
- [ ] `appsettings.Production.json` creado (NO en Git)
- [ ] Connection strings configurados
- [ ] Aplicación publicada en modo Release

## Despliegue

### Opción 1: Script Automatizado
```powershell
# Ejecutar como Administrador
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\deploy-to-iis.ps1 -PublishPath "C:\Publish\AttendanceSystem" -SiteName "AttendanceSystem"
```

### Opción 2: Manual

#### 1. Publicar Aplicación
```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.Blazor.Server"
dotnet publish -c Release -o "C:\Publish\AttendanceSystem" --runtime win-x64 --self-contained false
```

#### 2. Crear Application Pool
- [ ] Abrir IIS Manager
- [ ] Application Pools → Add Application Pool
- [ ] Name: `AttendanceSystemPool`
- [ ] .NET CLR version: **No Managed Code**
- [ ] Managed pipeline mode: Integrated

#### 3. Crear Sitio Web
- [ ] Sites → Add Website
- [ ] Site name: `AttendanceSystem`
- [ ] Application pool: `AttendanceSystemPool`
- [ ] Physical path: `C:\Publish\AttendanceSystem`
- [ ] Binding: HTTP, Port 80

#### 4. Configurar Permisos
```powershell
$path = "C:\Publish\AttendanceSystem"
$acl = Get-Acl $path
$permission = "IIS AppPool\AttendanceSystemPool","ReadAndExecute","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $path $acl
```

#### 5. Crear Carpeta de Logs
```powershell
New-Item -Path "C:\Publish\AttendanceSystem\logs" -ItemType Directory -Force

$path = "C:\Publish\AttendanceSystem\logs"
$acl = Get-Acl $path
$permission = "IIS AppPool\AttendanceSystemPool","Modify","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $path $acl
```

#### 6. Configurar Firewall
```powershell
New-NetFirewallRule -DisplayName "AttendanceSystem HTTP" -Direction Inbound -LocalPort 80 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "AttendanceSystem HTTPS" -Direction Inbound -LocalPort 443 -Protocol TCP -Action Allow
```

## Post-Despliegue

### Verificación
- [ ] Sitio accesible desde navegador local
- [ ] Sitio accesible desde otra máquina en la red
- [ ] Login funciona correctamente
- [ ] Conexión a base de datos funciona
- [ ] Dispositivos ZKTeco se conectan (si aplica)

### Pruebas
```powershell
# Probar localmente
Invoke-WebRequest -Uri "http://localhost" -UseBasicParsing

# Probar desde IP del servidor
Invoke-WebRequest -Uri "http://<IP_SERVIDOR>" -UseBasicParsing
```

### Logs
- [ ] Verificar logs de aplicación: `C:\Publish\AttendanceSystem\logs\`
- [ ] Verificar logs de IIS: `C:\inetpub\logs\LogFiles\`
- [ ] Verificar Event Viewer: Windows Logs → Application

## Actualización

### Usando Script
```powershell
# 1. Publicar nueva versión
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\src\Presentation\AttendanceSystem.Blazor.Server"
dotnet publish -c Release -o "C:\Temp\NewPublish" --runtime win-x64 --self-contained false

# 2. Ejecutar script de actualización
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\update-app.ps1 -NewVersionPath "C:\Temp\NewPublish"
```

### Manual
1. [ ] Detener sitio en IIS
2. [ ] Crear backup de carpeta actual
3. [ ] Copiar nuevos archivos (preservar appsettings.Production.json)
4. [ ] Aplicar migraciones si es necesario
5. [ ] Reiniciar sitio

## Rollback

```powershell
cd "c:\Users\B10 Caja 2\source\repos\AttendanceSystem\Docs"
.\rollback-app.ps1
```

## Troubleshooting

### Error 500.19
**Causa**: ASP.NET Core Module no instalado  
**Solución**: Instalar Hosting Bundle

### Error 502.5
**Causa**: Aplicación no puede iniciarse  
**Solución**: Revisar logs en `C:\Publish\AttendanceSystem\logs\stdout`

### Error de Conexión DB
**Verificar**:
- Connection string correcto
- PostgreSQL en ejecución
- Firewall permite conexión
- Usuario/contraseña correctos

### Sitio no accesible desde red
**Verificar**:
- Firewall de Windows permite puerto 80/443
- Binding configurado correctamente
- No hay otro servicio usando el puerto

## Comandos Útiles

```powershell
# Ver estado del sitio
Get-Website -Name "AttendanceSystem"

# Ver estado del Application Pool
Get-WebAppPoolState -Name "AttendanceSystemPool"

# Reiniciar sitio
Restart-WebAppPool -Name "AttendanceSystemPool"
Restart-Website -Name "AttendanceSystem"

# Ver logs recientes
Get-Content "C:\Publish\AttendanceSystem\logs\stdout_*.log" -Tail 50

# Probar conexión a PostgreSQL
Test-NetConnection -ComputerName <IP_POSTGRES> -Port 5432

# Ver procesos de la aplicación
Get-Process | Where-Object { $_.Path -like "*AttendanceSystem*" }
```

## Contactos de Emergencia

- **Administrador del Servidor**: _________________
- **DBA PostgreSQL**: _________________
- **Desarrollador Principal**: _________________
- **Soporte Técnico**: _________________

## Notas Adicionales

- Backups automáticos configurados: [ ] Sí [ ] No
- Monitoreo configurado: [ ] Sí [ ] No
- Certificado SSL instalado: [ ] Sí [ ] No
- Documentación actualizada: [ ] Sí [ ] No
