# Documentación del Sistema de Asistencia

Bienvenido a la documentación del AttendanceSystem. Aquí encontrarás guías, scripts y recursos para desplegar, mantener y solucionar problemas del sistema.

## 📖 Guías de Documentación

### [ARCHITECTURE.md](ARCHITECTURE.md)
**Arquitectura del Sistema**

Explica cómo está diseñado el sistema, por qué se separó en dos aplicaciones (Blazor + Servicio Windows), y cómo fluyen los datos.

**Lee esto si**:
- ✅ Quieres entender cómo funciona el sistema
- ✅ Necesitas saber por qué los dispositivos se gestionan desde la base de datos
- ✅ Quieres comprender la comunicación gRPC
- ✅ Eres nuevo en el proyecto

---

### [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)
**Guía de Despliegue en Producción**

Instrucciones completas para desplegar el sistema en producción, incluyendo configuración de ambos servicios, firewall, y verificación.

**Lee esto si**:
- ✅ Vas a desplegar el sistema por primera vez
- ✅ Necesitas configurar producción
- ✅ Tienes problemas de conectividad
- ✅ Quieres verificar que todo está funcionando correctamente

---

### [REINSTALL_SERVICE.md](REINSTALL_SERVICE.md)
**Reinstalación del Servicio Windows**

Guía paso a paso para eliminar y reinstalar el servicio Windows ZKTeco, especialmente útil después de correcciones de configuración.

**Lee esto si**:
- ✅ Necesitas actualizar el servicio con nueva configuración
- ✅ El servicio tiene problemas y quieres reinstalarlo
- ✅ Cambiaste archivos de configuración y necesitas aplicar cambios
- ✅ Quieres hacer una instalación limpia

---

## 🔧 Scripts de PowerShell

### [Diagnose-AttendanceSystem.ps1](Diagnose-AttendanceSystem.ps1)
**Script de Diagnóstico**

Verifica automáticamente:
- Estado del servicio Windows
- Puerto gRPC (5001)
- Reglas de firewall
- Configuración de archivos
- Conectividad a PostgreSQL
- Logs recientes y errores

**Ejecutar**:
```powershell
.\Docs\Diagnose-AttendanceSystem.ps1
```

**Cuándo usarlo**:
- ✅ Después de desplegar el sistema
- ✅ Cuando hay problemas de conectividad
- ✅ Para verificar configuración
- ✅ Como primer paso de troubleshooting

---

### [Reinstall-ZKTecoService.ps1](Reinstall-ZKTecoService.ps1)
**Script de Reinstalación Automatizada**

Automatiza completamente el proceso de:
1. Detener el servicio existente
2. Eliminar el servicio
3. Verificar archivos
4. Crear el servicio nuevamente
5. Configurar variables de entorno
6. Iniciar y verificar el servicio

**Ejecutar** (como Administrador):
```powershell
# Abrir PowerShell como Administrador
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
.\Docs\Reinstall-ZKTecoService.ps1
```

**Cuándo usarlo**:
- ✅ Después de actualizar archivos de configuración
- ✅ Para hacer una reinstalación limpia
- ✅ Cuando el servicio tiene problemas persistentes
- ✅ Después de actualizar el código del servicio

---

## 🚀 Inicio Rápido

### Primera Instalación

1. **Leer la arquitectura**:
   ```
   Docs/ARCHITECTURE.md
   ```

2. **Seguir guía de despliegue**:
   ```
   Docs/DEPLOYMENT_GUIDE.md
   ```

3. **Verificar instalación**:
   ```powershell
   .\Docs\Diagnose-AttendanceSystem.ps1
   ```

### Reinstalación del Servicio

1. **Copiar archivos actualizados** al servidor

2. **Ejecutar script de reinstalación**:
   ```powershell
   .\Docs\Reinstall-ZKTecoService.ps1
   ```

3. **Verificar**:
   ```powershell
   .\Docs\Diagnose-AttendanceSystem.ps1
   ```

### Solución de Problemas

1. **Ejecutar diagnóstico**:
   ```powershell
   .\Docs\Diagnose-AttendanceSystem.ps1
   ```

2. **Revisar guía de despliegue** - Sección "Solución de Problemas"

3. **Verificar logs**:
   - Event Viewer: `eventvwr.msc`
   - Logs de aplicación: `src/Presentation/AttendanceSystem.Blazor.Server/logs/`

---

## 📋 Checklist de Despliegue

### Antes de Desplegar

- [ ] PostgreSQL instalado y ejecutándose
- [ ] .NET Runtime instalado (versión correcta)
- [ ] Archivos compilados para win-x86 (servicio ZKTeco)
- [ ] Archivos de configuración actualizados

### Servicio ZKTeco

- [ ] Servicio instalado
- [ ] Servicio ejecutándose
- [ ] Puerto 5001 escuchando
- [ ] Variable de entorno `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Firewall configurado (si es necesario)

### Aplicación Blazor

- [ ] Conexión a PostgreSQL funcional
- [ ] URL del servicio ZKTeco correcta (`http://`, no `https://`)
- [ ] Migraciones de base de datos aplicadas
- [ ] Usuario inicial creado

### Verificación Final

- [ ] Script de diagnóstico ejecutado sin errores
- [ ] Dispositivos registrados en la base de datos
- [ ] Conexión a relojes checadores exitosa
- [ ] Descarga de registros funcional

---

## 🔍 Comandos Útiles

### Verificar Servicio
```powershell
# Estado del servicio
Get-Service "AttendanceSystem.ZKTeco.Service"

# Detalles completos
Get-Service "AttendanceSystem.ZKTeco.Service" | Select-Object *

# Iniciar servicio
Start-Service "AttendanceSystem.ZKTeco.Service"

# Detener servicio
Stop-Service "AttendanceSystem.ZKTeco.Service" -Force
```

### Verificar Puerto
```powershell
# Ver qué está escuchando en puerto 5001
netstat -ano | findstr :5001

# Usando PowerShell
Get-NetTCPConnection -LocalPort 5001 -State Listen

# Ver proceso que usa el puerto
Get-Process -Id (Get-NetTCPConnection -LocalPort 5001).OwningProcess
```

### Verificar Conectividad
```powershell
# Ping al reloj checador
ping 192.168.1.100

# Test de puerto
Test-NetConnection -ComputerName 192.168.1.100 -Port 4370

# Ping a PostgreSQL
Test-Connection -ComputerName localhost -Count 2
```

### Ver Logs
```powershell
# Event Viewer
eventvwr.msc

# Logs recientes del servicio
Get-EventLog -LogName Application -Source "AttendanceSystem.ZKTeco.Service" -Newest 10

# Logs de la aplicación
Get-Content "src\Presentation\AttendanceSystem.Blazor.Server\logs\attendance-system-errors-*.log" -Tail 50
```

---

## 📞 Soporte

Si después de revisar toda la documentación y ejecutar los scripts de diagnóstico sigues teniendo problemas:

1. **Recopila información**:
   - Salida del script de diagnóstico
   - Logs de Event Viewer
   - Logs de la aplicación
   - Configuración actual (sin contraseñas)

2. **Verifica**:
   - ¿Seguiste todos los pasos de la guía?
   - ¿Ejecutaste el script de diagnóstico?
   - ¿Revisaste los logs?

3. **Contacta** con:
   - Descripción detallada del problema
   - Pasos para reproducir
   - Información recopilada

---

## 📝 Notas Importantes

### Configuración de Dispositivos

⚠️ **IMPORTANTE**: Los relojes checadores **NO se configuran en archivos de configuración**.

- ✅ Se registran desde la interfaz web
- ✅ Se guardan en la base de datos PostgreSQL
- ✅ Soporta múltiples dispositivos
- ✅ Se pueden actualizar dinámicamente

### Protocolo gRPC

⚠️ **IMPORTANTE**: El servicio usa **HTTP** (sin TLS) por defecto.

- ✅ URL debe ser `http://localhost:5001`
- ❌ NO usar `https://`
- 🔒 Para producción, considerar implementar TLS

### Arquitectura x86

⚠️ **IMPORTANTE**: El servicio ZKTeco debe compilarse para **win-x86** (32-bit).

- ✅ El SDK de ZKTeco solo funciona en x86
- ✅ La aplicación Blazor puede ser x64
- ✅ Se comunican vía gRPC

---

## 🔄 Actualizaciones

Este directorio de documentación se actualiza con:
- Nuevas guías según necesidades
- Scripts mejorados
- Soluciones a problemas comunes
- Mejores prácticas

**Última actualización**: 2026-01-27
