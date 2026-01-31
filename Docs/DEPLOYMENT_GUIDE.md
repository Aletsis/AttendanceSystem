# Guía de Despliegue en Producción - AttendanceSystem

## Recursos Adicionales

📚 **Documentación relacionada**:
- [Arquitectura del Sistema](ARCHITECTURE.md) - Entender cómo funciona el sistema
- [Reinstalar Servicio Windows](REINSTALL_SERVICE.md) - Guía para eliminar y reinstalar el servicio
- [Script de Diagnóstico](Diagnose-AttendanceSystem.ps1) - Verificar configuración y conectividad
- [Script de Reinstalación](Reinstall-ZKTecoService.ps1) - Automatizar reinstalación del servicio

---

## Problema Identificado

El sistema no puede conectarse a los relojes checadores en producción debido a una configuración incorrecta de la comunicación gRPC entre:
- **AttendanceSystem.Blazor.Server** (Aplicación Web)
- **AttendanceSystem.ZKTeco.Service** (Servicio Windows que se comunica con los relojes)

## Cambios Realizados

### 1. Corrección de Protocolo HTTP/HTTPS
- **Problema**: La aplicación Blazor intentaba conectarse usando `https://` pero el servicio gRPC está configurado para HTTP sin TLS
- **Solución**: Cambiar la URL de `https://localhost:5001` a `http://localhost:5001`

### 2. Configuración del Cliente gRPC
Se agregó configuración adicional al cliente gRPC en `Program.cs` para:
- Permitir conexiones HTTP inseguras
- Configurar tamaños máximos de mensaje
- Manejar certificados correctamente

### 3. Archivos de Configuración de Producción
Se crearon archivos `appsettings.Production.json` para ambos servicios.

## Pasos para Despliegue en Producción

### Escenario 1: Ambos servicios en el mismo servidor

1. **Configurar el Servicio ZKTeco**
   - Editar `appsettings.Production.json` del servicio ZKTeco:
   ```json
   {
     "GrpcPort": 5001
   }
   ```
   
   **Nota**: La IP y puerto de los relojes checadores NO se configuran aquí. Se registran en la base de datos a través de la interfaz web.

2. **Configurar la Aplicación Blazor**
   - Editar `appsettings.Production.json` de Blazor:
   ```json
   {
     "ZKTecoService": {
       "Url": "http://localhost:5001"
     },
     "ConnectionStrings": {
       "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=TU_PASSWORD;"
     }
   }
   ```

3. **Instalar el Servicio Windows**
   ```powershell
   # Navegar al directorio de publicación
   cd "C:\Path\To\AttendanceSystem.ZKTeco.Service"
   
   # Crear el servicio
   sc.exe create "AttendanceSystem.ZKTeco.Service" binPath= "C:\Path\To\AttendanceSystem.ZKTeco.Service.exe" start= auto
   
   # Iniciar el servicio
   sc.exe start "AttendanceSystem.ZKTeco.Service"
   ```

4. **Verificar que el servicio está escuchando**
   ```powershell
   netstat -ano | findstr :5001
   ```

5. **Registrar los Relojes Checadores**
   - Acceder a la aplicación web
   - Ir a la sección de "Dispositivos"
   - Registrar cada reloj checador con su IP, puerto y ubicación
   - Los dispositivos se guardan en la base de datos PostgreSQL

### Escenario 2: Servicios en servidores diferentes

1. **Configurar el Servicio ZKTeco**
   - En el servidor donde está el reloj checador:
   ```json
   {
     "GrpcPort": 5001
   }
   ```

2. **Configurar la Aplicación Blazor**
   - En el servidor web:
   ```json
   {
     "ZKTecoService": {
       "Url": "http://IP_SERVIDOR_ZKTECO:5001"
     }
   }
   ```

3. **Configurar Firewall**
   - En el servidor del servicio ZKTeco, abrir el puerto 5001:
   ```powershell
   New-NetFirewallRule -DisplayName "ZKTeco gRPC Service" -Direction Inbound -LocalPort 5001 -Protocol TCP -Action Allow
   ```

4. **Registrar Dispositivos**
   - Los relojes checadores se registran desde la interfaz web
   - Cada dispositivo debe ser accesible desde el servidor donde corre el servicio ZKTeco

## Solución de Problemas

### Error: "RpcException: Status(StatusCode=Unavailable)"

**Causa**: El servicio ZKTeco no está ejecutándose o no es accesible.

**Soluciones**:
1. Verificar que el servicio Windows está ejecutándose:
   ```powershell
   Get-Service "AttendanceSystem.ZKTeco.Service"
   ```

2. Verificar que está escuchando en el puerto correcto:
   ```powershell
   netstat -ano | findstr :5001
   ```

3. Verificar logs del servicio en:
   - Event Viewer de Windows
   - Logs de la aplicación (si están configurados)

### Error: "The SSL connection could not be established"

**Causa**: Intento de usar HTTPS cuando el servicio solo acepta HTTP.

**Solución**: Verificar que la URL en `appsettings.json` use `http://` y no `https://`

### Error: No se puede conectar al reloj checador

**Causa**: El servicio ZKTeco no puede alcanzar el dispositivo físico.

**Soluciones**:
1. Verificar conectividad de red:
   ```powershell
   ping IP_DEL_RELOJ
   Test-NetConnection -ComputerName IP_DEL_RELOJ -Port 4370
   ```

2. Verificar que la IP y puerto en `appsettings.Production.json` son correctos

3. Verificar que el reloj checador está encendido y en la red

### Error: "Cannot access a disposed object"

**Causa**: Problemas con el ciclo de vida del cliente gRPC.

**Solución**: Verificar que el servicio está registrado correctamente como Scoped en el DI container.

## Verificación de Despliegue

### 1. Verificar el Servicio ZKTeco
```powershell
# Verificar estado del servicio
Get-Service "AttendanceSystem.ZKTeco.Service"

# Verificar que está escuchando
netstat -ano | findstr :5001
```

### 2. Probar Conectividad desde Blazor
Desde la aplicación web, intentar:
- Conectar a un dispositivo
- Obtener información del dispositivo
- Descargar registros de asistencia

### 3. Revisar Logs
- Logs de la aplicación Blazor en `logs/attendance-system-.log`
- Logs del servicio ZKTeco en Event Viewer
- Logs de PostgreSQL en la tabla `Logs`

## Configuración Recomendada para Producción

### Seguridad
⚠️ **IMPORTANTE**: El servicio gRPC actualmente usa HTTP sin encriptación. Para producción, considere:

1. **Usar TLS/SSL**:
   - Configurar certificados SSL en el servicio ZKTeco
   - Actualizar la URL a `https://`
   - Configurar Kestrel para usar HTTPS

2. **Autenticación**:
   - Implementar autenticación en el servicio gRPC
   - Usar tokens de acceso

3. **Firewall**:
   - Restringir acceso al puerto 5001 solo desde el servidor web
   - No exponer el puerto a Internet

### Monitoreo
1. Configurar alertas para:
   - Servicio ZKTeco caído
   - Errores de conexión con relojes
   - Fallas en sincronización

2. Revisar logs regularmente

### Respaldo
1. Configurar respaldo automático de la base de datos PostgreSQL
2. Mantener logs históricos
3. Documentar configuración de red y dispositivos

## Contacto y Soporte

Para problemas adicionales:
1. Revisar logs detallados
2. Verificar configuración de red
3. Contactar al equipo de desarrollo con:
   - Logs de error
   - Configuración actual
   - Descripción del problema
