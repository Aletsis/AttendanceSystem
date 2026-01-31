# AttendanceSystem - Sistema de Control de Asistencia

Sistema de control de asistencia con integración a dispositivos biométricos ZKTeco, desarrollado con **Clean Architecture** y **Domain-Driven Design (DDD)**.

## 🏗️ Arquitectura del Proyecto

El proyecto sigue los principios de Clean Architecture con las siguientes capas:

### **Core Layer**
- **`AttendanceSystem.Domain`**: Entidades, Value Objects, Enumerations, Eventos de Dominio
- **`AttendanceSystem.Aplication`**: Casos de uso (Comandos, Queries, Handlers con MediatR)

### **Infrastructure Layer**
- **`AttendanceSystem.Infrastructure`**: Persistencia (EF Core), Repositorios, Servicios externos
- **`AttendanceSystem.ZKTeco`**: ⚠️ Integración con dispositivos ZKTeco (requiere configuración)

### **Presentation Layer**
- **`AttendanceSystem.Blazor.Server`**: Aplicación web Blazor Server con MudBlazor
- **`AttendanceSystem.ZKTeco.Service`**: ⚠️ Servicio Windows gRPC (requiere configuración)

### **Shared Layer**
- **`AttendanceSystem.Contracts`**: DTOs compartidos entre capas

## ✅ Estado Actual

### Completado
- ✅ Estructura de solución y proyectos creada
- ✅ Clases base del dominio (AggregateRoot, Entity, DomainEvent, Enumeration)
- ✅ Value Objects (AttendanceRecordId, DeviceId, EmployeeId)
- ✅ Enumerations (CheckType, VerifyMethod, AttendanceStatus, DeviceStatus)
- ✅ Entidades de dominio (AttendanceRecord, Device)
- ✅ Repositorios (IAttendanceRepository, IDeviceRepository)
- ✅ Configuración de EF Core con conversiones de Value Objects
- ✅ Comandos y Queries con MediatR
- ✅ Event Handlers para eventos de dominio
- ✅ Aplicación Blazor Server con MudBlazor configurada
- ✅ Implementación stub del cliente gRPC

### ⚠️ Pendiente de Configuración

#### 1. Servicio gRPC para ZKTeco
Los proyectos `AttendanceSystem.ZKTeco` y `AttendanceSystem.ZKTeco.Service` requieren:

1. **Crear archivo `.proto`** en `src/Infrastructure/AttendanceSystem.ZKTeco/Protos/zkteco.proto`:
```protobuf
syntax = "proto3";

option csharp_namespace = "AttendanceSystem.ZKTeco.Grpc";

service ZKTecoService {
  rpc ConnectDevice (ConnectDeviceRequest) returns (ConnectDeviceResponse);
  rpc GetAttendanceLogs (GetAttendanceLogsRequest) returns (GetAttendanceLogsResponse);
  rpc ClearDeviceLogs (ClearDeviceLogsRequest) returns (ClearDeviceLogsResponse);
  rpc DisconnectDevice (DisconnectDeviceRequest) returns (DisconnectDeviceResponse);
}

message ConnectDeviceRequest {
  string ip_address = 1;
  int32 port = 2;
}

message ConnectDeviceResponse {
  bool success = 1;
  string message = 2;
}

message GetAttendanceLogsRequest {
  string device_id = 1;
  string from_date = 2;
}

message GetAttendanceLogsResponse {
  repeated AttendanceRecord records = 1;
}

message AttendanceRecord {
  string device_id = 1;
  string user_id = 2;
  string check_time = 3;
  int32 verify_mode = 4;
  int32 in_out_mode = 5;
  int32 work_code = 6;
}

message ClearDeviceLogsRequest {
  string device_id = 1;
}

message ClearDeviceLogsResponse {
  bool success = 1;
}

message DisconnectDeviceRequest {
}

message DisconnectDeviceResponse {
  bool success = 1;
}
```

2. **Actualizar `.csproj` de ZKTeco** para incluir el archivo .proto:
```xml
<ItemGroup>
  <Protobuf Include="Protos\zkteco.proto" GrpcServices="Server" />
</ItemGroup>
```

3. **Actualizar `.csproj` de Infrastructure** para incluir el archivo .proto como cliente:
```xml
<ItemGroup>
  <Protobuf Include="..\AttendanceSystem.ZKTeco\Protos\zkteco.proto" GrpcServices="Client" />
</ItemGroup>
```

4. **Descomentar en `AttendanceSystem.sln`** las líneas de Build para los proyectos ZKTeco (líneas 48, 50, 56, 58)

5. **Descomentar en `Program.cs`** la configuración del cliente gRPC (líneas 46-49)

#### 2. Biblioteca zkemkeeper.dll
El proyecto ZKTeco requiere la biblioteca nativa `zkemkeeper.dll`:

1. Obtener `zkemkeeper.dll` del SDK de ZKTeco
2. Copiarla a `src/Infrastructure/AttendanceSystem.ZKTeco/lib/zkemkeeper.dll`
3. Asegurarse de que el proyecto esté configurado para x86 (ya configurado)

#### 3. Base de Datos
1. Actualizar la cadena de conexión en `appsettings.json`
2. Ejecutar migraciones:
```bash
dotnet ef migrations add InitialCreate --project src/Infrastructure/AttendanceSystem.Infrastructure --startup-project src/Presentation/AttendanceSystem.Blazor.Server
dotnet ef database update --project src/Infrastructure/AttendanceSystem.Infrastructure --startup-project src/Presentation/AttendanceSystem.Blazor.Server
```

#### 4. Configuración de SendGrid (Opcional)
Actualizar en `appsettings.json`:
```json
"SendGrid": {
  "ApiKey": "TU_API_KEY_DE_SENDGRID",
  "FromEmail": "tu-email@dominio.com",
  "AlertEmail": "alertas@dominio.com"
}
```

## 🚀 Cómo Ejecutar

### Opción 1: Solo la aplicación web (sin ZKTeco)
```bash
cd src/Presentation/AttendanceSystem.Blazor.Server
dotnet run
```

### Opción 2: Con servicio ZKTeco (después de configurar gRPC)
1. Ejecutar el servicio Windows:
```bash
cd src/Presentation/AttendanceSystem.ZKTeco.Service
dotnet run
```

2. En otra terminal, ejecutar la aplicación web:
```bash
cd src/Presentation/AttendanceSystem.Blazor.Server
dotnet run
```

## 📦 Paquetes NuGet Utilizados

- **MediatR** (12.2.0) - CQRS y Mediator pattern
- **Entity Framework Core** (8.0.0) - ORM
- **MudBlazor** (6.11.2) - Componentes UI para Blazor
- **Hangfire** (1.8.9) - Tareas programadas en segundo plano
- **SendGrid** (9.29.3) - Envío de emails
- **Grpc.AspNetCore** (2.60.0) - Servidor gRPC
- **Grpc.Net.Client** (2.60.0) - Cliente gRPC

## 🔧 Tecnologías

- .NET 8.0
- Blazor Server
- SQL Server
- gRPC
- Clean Architecture
- Domain-Driven Design (DDD)
- CQRS con MediatR

## 📝 Notas Importantes

1. Los proyectos ZKTeco están temporalmente deshabilitados en la solución hasta que se configure gRPC
2. El cliente gRPC actual es una implementación stub que retorna valores por defecto
3. Se requiere SQL Server para la persistencia de datos
4. El proyecto está configurado para x86 debido a la dependencia de zkemkeeper.dll

## 🤝 Contribuir

Para continuar el desarrollo:
1. Configurar los archivos .proto para gRPC
2. Implementar el servicio Windows para comunicación con dispositivos ZKTeco
3. Crear las migraciones de base de datos
4. Implementar la UI de Blazor para gestión de dispositivos y empleados
5. Agregar pruebas unitarias e integración

## 📄 Licencia

[Especificar licencia]
