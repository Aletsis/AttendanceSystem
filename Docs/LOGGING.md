# Sistema de Logging - AttendanceSystem

## Descripción General

El sistema de asistencia ahora cuenta con un sistema de logging completo implementado con **Serilog**, que proporciona:

- ✅ Logging estructurado y configurable
- ✅ Múltiples destinos (consola, archivos, base de datos)
- ✅ Logging automático de todas las solicitudes MediatR
- ✅ Logging automático de requests HTTP
- ✅ Métricas de rendimiento (tiempo de ejecución)
- ✅ Niveles de log configurables por ambiente
- ✅ Rotación automática de archivos

## Arquitectura del Sistema de Logging

### Componentes Principales

1. **LoggingConfiguration** (`Infrastructure/Logging/LoggingConfiguration.cs`)
   - Configuración centralizada de Serilog
   - Define sinks (destinos de logs)
   - Configura enriquecedores (enrichers)

2. **LoggingBehavior** (`Infrastructure/Logging/LoggingBehavior.cs`)
   - Pipeline behavior de MediatR
   - Registra automáticamente todos los comandos y queries
   - Mide tiempo de ejecución
   - Captura excepciones

3. **RequestLoggingMiddleware** (`Infrastructure/Logging/RequestLoggingMiddleware.cs`)
   - Middleware para logging de HTTP requests
   - Registra método, ruta, código de estado
   - Mide tiempo de respuesta

## Destinos de Logs (Sinks)

### 1. Consola
- **Uso**: Desarrollo y debugging
- **Formato**: `[HH:mm:ss LEVEL] Mensaje`
- **Nivel mínimo**: Warning (en todos los ambientes para reducir ruido)

### 2. Archivos
Se generan dos tipos de archivos en la carpeta `logs/`:

#### Archivo General
- **Nombre**: `attendance-system-YYYYMMDD.log`
- **Contenido**: Todos los logs (Information y superior)
- **Rotación**: Diaria
- **Retención**: 30 días
- **Tamaño máximo**: 10 MB por archivo

#### Archivo de Errores
- **Nombre**: `attendance-system-errors-YYYYMMDD.log`
- **Contenido**: Solo errores (Error y Fatal)
- **Rotación**: Diaria
- **Retención**: 90 días

### 3. Base de Datos SQL Server
- **Tabla**: `dbo.Logs`
- **Nivel mínimo**: Information
- **Creación automática**: Sí
- **Batch**: 50 registros cada 5 segundos
- **Columnas adicionales**: UserName, MachineName, Application

## Niveles de Log

| Nivel | Uso | Ejemplo |
|-------|-----|---------|
| **Debug** | Información detallada para debugging | Variables, estados internos |
| **Information** | Eventos normales del sistema | Inicio de operaciones, completadas exitosamente |
| **Warning** | Situaciones anormales pero manejables | Reintentos, datos faltantes opcionales |
| **Error** | Errores que afectan la operación actual | Excepciones capturadas, validaciones fallidas |
| **Fatal** | Errores críticos que detienen la aplicación | Fallo de conexión a BD, configuración inválida |

## Uso en el Código

### 1. En Command/Query Handlers

El logging es **automático** gracias al `LoggingBehavior`. No necesitas agregar código adicional.

```csharp
public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, EmployeeDto>
{
    // El LoggingBehavior registrará automáticamente:
    // - Inicio de la solicitud con los datos del comando
    // - Tiempo de ejecución
    // - Resultado o excepción
    
    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        // Tu lógica aquí
    }
}
```

### 2. Logging Manual en Servicios

Para logging adicional o específico, inyecta `ILogger<T>`:

```csharp
using Microsoft.Extensions.Logging;

public class AttendanceCalculationService
{
    private readonly ILogger<AttendanceCalculationService> _logger;

    public AttendanceCalculationService(ILogger<AttendanceCalculationService> logger)
    {
        _logger = logger;
    }

    public async Task CalculateAttendance(EmployeeId employeeId, DateOnly date)
    {
        _logger.LogInformation(
            "Calculando asistencia para empleado {EmployeeId} en fecha {Date}",
            employeeId.Value,
            date);

        try
        {
            // Lógica de cálculo
            var result = await PerformCalculation();
            
            _logger.LogInformation(
                "Asistencia calculada exitosamente: {Result}",
                result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al calcular asistencia para empleado {EmployeeId}",
                employeeId.Value);
            throw;
        }
    }
}
```

### 3. Logging Estructurado

Usa propiedades estructuradas en lugar de interpolación de strings:

```csharp
// ✅ CORRECTO - Logging estructurado
_logger.LogInformation(
    "Usuario {UserId} descargó {RecordCount} registros del dispositivo {DeviceId}",
    userId,
    records.Count,
    deviceId);

// ❌ INCORRECTO - Interpolación de strings
_logger.LogInformation($"Usuario {userId} descargó {records.Count} registros");
```

### 4. Niveles de Log Apropiados

```csharp
// Debug - Información muy detallada
_logger.LogDebug("Procesando registro: {@Record}", record);

// Information - Flujo normal
_logger.LogInformation("Descarga de logs completada: {Count} registros", count);

// Warning - Situación inusual pero manejable
_logger.LogWarning("Empleado {EmployeeId} no tiene turno asignado, usando turno por defecto", employeeId);

// Error - Error en operación actual
_logger.LogError(ex, "Error al conectar con dispositivo {DeviceId}", deviceId);

// Fatal - Error crítico
_logger.LogCritical(ex, "No se pudo conectar a la base de datos");
```

## Configuración

### appsettings.json (Producción)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "restrictedToMinimumLevel": "Warning",
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

### appsettings.Development.json (Desarrollo)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "AttendanceSystem": "Debug"
      }
    }
  }
}
```

## Consultar Logs

### 1. Archivos de Log

Los archivos se encuentran en la carpeta `logs/` en la raíz del proyecto:

```
logs/
├── attendance-system-20260109.log          # Logs generales
├── attendance-system-20260110.log
├── attendance-system-errors-20260109.log   # Solo errores
└── attendance-system-errors-20260110.log
```

### 2. Base de Datos

Consulta la tabla `Logs` en SQL Server:

```sql
-- Ver los últimos 100 logs
SELECT TOP 100 
    TimeStamp,
    Level,
    Message,
    Exception,
    MachineName,
    Application
FROM dbo.Logs
ORDER BY TimeStamp DESC;

-- Ver solo errores de hoy
SELECT *
FROM dbo.Logs
WHERE Level = 'Error'
  AND CAST(TimeStamp AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY TimeStamp DESC;

-- Buscar logs de un comando específico
SELECT *
FROM dbo.Logs
WHERE Message LIKE '%CreateEmployeeCommand%'
ORDER BY TimeStamp DESC;
```

### 3. Consola

Durante el desarrollo, los logs aparecen en la consola con colores:

```
[17:45:23 INF] Iniciando AttendanceSystem.Blazor.Server
[17:45:24 INF] Iniciando solicitud: CreateEmployeeCommand
[17:45:24 INF] Solicitud completada: CreateEmployeeCommand - Tiempo: 245ms
```

## Mejores Prácticas

### ✅ DO

1. **Usar logging estructurado** con propiedades nombradas
2. **Incluir contexto relevante** (IDs, nombres, fechas)
3. **Usar el nivel apropiado** según la importancia
4. **Capturar excepciones** con `LogError(ex, ...)`
5. **Medir rendimiento** en operaciones críticas

### ❌ DON'T

1. **No registrar información sensible** (contraseñas, tokens)
2. **No usar interpolación de strings** en mensajes de log
3. **No hacer logging excesivo** en loops (usar Debug)
4. **No duplicar logs** (el LoggingBehavior ya registra comandos)
5. **No ignorar excepciones** sin registrarlas

## Monitoreo y Alertas

### Logs Importantes a Monitorear

1. **Errores de conexión a dispositivos**
   ```csharp
   _logger.LogError(ex, "Error al conectar con dispositivo {DeviceId}", deviceId);
   ```

2. **Fallos en cálculo de asistencia**
   ```csharp
   _logger.LogError(ex, "Error al calcular asistencia diaria para {Date}", date);
   ```

3. **Problemas de sincronización**
   ```csharp
   _logger.LogWarning("Descarga de logs falló para dispositivo {DeviceId}, reintentando...", deviceId);
   ```

4. **Operaciones lentas**
   ```csharp
   if (stopwatch.ElapsedMilliseconds > 5000)
   {
       _logger.LogWarning("Operación lenta detectada: {Operation} - {ElapsedMs}ms", 
           operationName, stopwatch.ElapsedMilliseconds);
   }
   ```

## Troubleshooting

### Los logs no aparecen en la base de datos

1. Verificar que la tabla `Logs` existe
2. Verificar la cadena de conexión en `appsettings.json`
3. Revisar permisos de escritura en la base de datos

### Los archivos de log no se crean

1. Verificar permisos de escritura en la carpeta `logs/`
2. La carpeta se crea automáticamente en el primer log
3. Revisar la configuración de `path` en `appsettings.json`

### Demasiados logs en Development

Ajustar el nivel mínimo en `appsettings.Development.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"  // Cambiar de Debug a Information
    }
  }
}
```

## Próximos Pasos

Posibles mejoras futuras:

- [ ] Integración con Application Insights o Seq
- [ ] Dashboard de logs en tiempo real
- [ ] Alertas automáticas por email para errores críticos
- [ ] Exportación de logs a formatos externos
- [ ] Métricas y estadísticas de rendimiento

## Referencias

- [Serilog Documentation](https://serilog.net/)
- [Structured Logging Best Practices](https://github.com/serilog/serilog/wiki/Structured-Data)
- [ASP.NET Core Logging](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/)
