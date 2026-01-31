# Arquitectura del Sistema de Asistencia

## Resumen Ejecutivo

El sistema de asistencia está compuesto por dos aplicaciones principales que se comunican vía gRPC:

1. **AttendanceSystem.Blazor.Server** - Aplicación web (interfaz de usuario)
2. **AttendanceSystem.ZKTeco.Service** - Servicio Windows (comunicación con hardware)

## ¿Por qué dos aplicaciones separadas?

### El Problema: Arquitectura x86 vs x64

Los relojes checadores ZKTeco requieren el SDK oficial que **solo funciona en arquitectura x86 (32-bit)**. Sin embargo, las aplicaciones modernas de .NET prefieren ejecutarse en x64 (64-bit) para mejor rendimiento.

### La Solución: Separación de Responsabilidades

```
┌─────────────────────────────────────┐
│  AttendanceSystem.Blazor.Server     │
│  (x64 - Aplicación Web Principal)   │
│  - Interfaz de usuario              │
│  - Lógica de negocio                │
│  - Base de datos PostgreSQL         │
│  - Gestión de empleados             │
│  - Reportes                         │
└──────────────┬──────────────────────┘
               │ gRPC (HTTP/2)
               │ Puerto 5001
               ▼
┌─────────────────────────────────────┐
│  AttendanceSystem.ZKTeco.Service    │
│  (x86 - Servicio Windows)           │
│  - SDK ZKTeco (32-bit)              │
│  - Comunicación con relojes         │
│  - Descarga de registros            │
│  - Gestión de dispositivos          │
└──────────────┬──────────────────────┘
               │ TCP/IP
               │ Puerto 4370 (default)
               ▼
┌─────────────────────────────────────┐
│  Reloj Checador ZKTeco              │
│  - Almacena registros de asistencia │
│  - Verifica huellas/rostros         │
│  - Gestiona usuarios                │
└─────────────────────────────────────┘
```

## Flujo de Datos

### 1. Registro de Dispositivos

Los dispositivos **NO se configuran en archivos de configuración**. Se registran dinámicamente:

```
Usuario → Interfaz Web → Base de Datos
```

**Tabla: Devices**
```sql
CREATE TABLE Devices (
    Id VARCHAR PRIMARY KEY,
    Name VARCHAR NOT NULL,
    IpAddress VARCHAR NOT NULL,  -- ← IP del reloj checador
    Port INT NOT NULL,            -- ← Puerto del reloj (4370)
    Location VARCHAR,
    IsActive BOOLEAN,
    Status INT,
    ...
)
```

### 2. Conexión a un Reloj Checador

```
1. Usuario hace clic en "Conectar" en la interfaz web
2. Blazor App lee el Device de la base de datos
3. Blazor App → gRPC Request → ZKTeco Service
   {
     ipAddress: "192.168.1.100",  // Desde DB
     port: 4370                    // Desde DB
   }
4. ZKTeco Service → SDK x86 → Reloj Checador
5. Respuesta: Conectado / Error
```

### 3. Descarga de Registros

```
1. Usuario solicita descarga (manual o automática vía Hangfire)
2. Blazor App → gRPC → ZKTeco Service
3. ZKTeco Service:
   - Conecta al dispositivo (usando IP/Puerto de la solicitud)
   - Descarga registros del SDK
   - Retorna datos vía gRPC
4. Blazor App:
   - Procesa registros
   - Aplica reglas de negocio
   - Guarda en base de datos
```

## Configuración Correcta

### AttendanceSystem.ZKTeco.Service (appsettings.json)

```json
{
  "GrpcPort": 5001
}
```

**Nota**: NO hay configuración de dispositivos aquí. Los dispositivos se pasan como parámetros en cada llamada gRPC.

### AttendanceSystem.Blazor.Server (appsettings.json)

```json
{
  "ZKTecoService": {
    "Url": "http://localhost:5001"  // ← HTTP, no HTTPS
  },
  "ConnectionStrings": {
    "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;..."
  }
}
```

## Protocolo gRPC

### ¿Por qué HTTP en lugar de HTTPS?

Actualmente el servicio usa **HTTP sin TLS** por simplicidad en desarrollo y despliegues locales. 

**Para producción**, se recomienda:
1. Configurar certificados SSL/TLS
2. Cambiar `ListenAnyIP` a `ListenLocalhost` si ambos servicios están en el mismo servidor
3. Usar autenticación en las llamadas gRPC

### Métodos gRPC Disponibles

```protobuf
service ZKTecoService {
  rpc ConnectDevice(ConnectDeviceRequest) returns (ConnectDeviceResponse);
  rpc GetAttendanceLogs(GetAttendanceLogsRequest) returns (GetAttendanceLogsResponse);
  rpc ClearDeviceLogs(ClearDeviceLogsRequest) returns (ClearDeviceLogsResponse);
  rpc DisconnectDevice(DisconnectDeviceRequest) returns (DisconnectDeviceResponse);
  rpc GetDeviceInfo(GetDeviceInfoRequest) returns (GetDeviceInfoResponse);
  rpc RegisterEmployee(RegisterEmployeeRequest) returns (RegisterEmployeeResponse);
  rpc DeleteEmployee(DeleteEmployeeRequest) returns (DeleteEmployeeResponse);
  // ... más métodos
}
```

## Clean Architecture

El sistema sigue principios de Clean Architecture:

```
┌─────────────────────────────────────────────────┐
│ Presentation Layer                              │
│ - AttendanceSystem.Blazor.Server (UI)           │
│ - AttendanceSystem.ZKTeco.Service (gRPC Server) │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ Application Layer                               │
│ - Use Cases (Commands/Queries)                  │
│ - DTOs                                          │
│ - Interfaces (IZKTecoDeviceClient)              │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ Domain Layer                                    │
│ - Entities (Device, Employee, Attendance)       │
│ - Value Objects (DeviceId, EmployeeId)          │
│ - Domain Events                                 │
│ - Business Rules                                │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ Infrastructure Layer                            │
│ - AttendanceSystem.Infrastructure (EF Core)     │
│ - AttendanceSystem.ZKTeco (SDK Adapter x86)     │
│ - GrpcZKTecoDeviceClient (gRPC Client)          │
└─────────────────────────────────────────────────┘
```

### Adaptadores

**IZKTecoDeviceClient** es la interfaz (puerto) definida en Application Layer.

Tiene **dos implementaciones** (adaptadores):

1. **ZKTecoDeviceClient** (Infrastructure.ZKTeco)
   - Usa el SDK nativo x86
   - Se ejecuta en el servicio Windows
   - Comunicación directa con hardware

2. **GrpcZKTecoDeviceClient** (Infrastructure)
   - Cliente gRPC
   - Se ejecuta en la aplicación Blazor
   - Delega al servicio Windows vía gRPC

## Gestión de Dispositivos

### Agregar un Nuevo Reloj Checador

1. **Desde la Interfaz Web**:
   - Navegar a "Dispositivos"
   - Clic en "Agregar Dispositivo"
   - Ingresar:
     - ID único (ej: "RELOJ-001")
     - Nombre descriptivo
     - IP del reloj (ej: "192.168.1.100")
     - Puerto (generalmente 4370)
     - Ubicación (opcional)

2. **El sistema guarda en la base de datos**:
   ```csharp
   var device = Device.Create(
       deviceId: "RELOJ-001",
       name: "Reloj Entrada Principal",
       ipAddress: "192.168.1.100",
       port: 4370,
       location: "Planta Baja"
   );
   await _deviceRepository.AddAsync(device);
   ```

3. **Probar Conexión**:
   - Clic en "Conectar"
   - El sistema envía la IP/Puerto al servicio ZKTeco
   - Verifica conectividad

### Múltiples Dispositivos

El sistema soporta **múltiples relojes checadores**:
- Cada uno con su propia IP y configuración
- Todos gestionados desde la misma interfaz
- Descargas pueden ser individuales o masivas
- Hangfire puede programar descargas automáticas

## Troubleshooting

### Error: "Cannot connect to device"

**Verificar**:
1. ¿El reloj está encendido y en la red?
   ```powershell
   ping 192.168.1.100
   Test-NetConnection -ComputerName 192.168.1.100 -Port 4370
   ```

2. ¿El servicio ZKTeco está ejecutándose?
   ```powershell
   Get-Service "AttendanceSystem.ZKTeco.Service"
   ```

3. ¿La IP en la base de datos es correcta?
   - Verificar en la interfaz web
   - Actualizar si es necesario

### Error: "RpcException: Unavailable"

**Causa**: La aplicación Blazor no puede comunicarse con el servicio ZKTeco.

**Verificar**:
1. ¿El servicio está escuchando en el puerto 5001?
   ```powershell
   netstat -ano | findstr :5001
   ```

2. ¿La URL es correcta en appsettings.json?
   - Debe ser `http://localhost:5001` (HTTP, no HTTPS)

3. ¿Hay firewall bloqueando?
   - Crear regla para permitir puerto 5001

## Conclusión

**Puntos Clave**:
1. ✅ Los dispositivos se registran en la **base de datos**, no en archivos de configuración
2. ✅ El servicio ZKTeco es **stateless** - recibe IP/Puerto en cada llamada
3. ✅ La comunicación gRPC usa **HTTP** (sin TLS por ahora)
4. ✅ La arquitectura permite **múltiples dispositivos** fácilmente
5. ✅ La separación x86/x64 resuelve limitaciones del SDK

Esta arquitectura es **escalable**, **mantenible** y sigue **mejores prácticas** de diseño de software.
