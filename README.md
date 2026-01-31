# AttendanceSystem - Sistema de Control de Asistencia

Sistema moderno de control de asistencia desarrollado en .NET 9 con Blazor Server, integración a dispositivos biométricos ZKTeco y base de datos PostgreSQL. Diseñado bajo los principios de **Clean Architecture** y **Domain-Driven Design (DDD)**.

## 🚀 Características Principales

-   **Dashboard Interactivo**: Visualización de métricas de asistencia, empleados presentes, ausencias y retardos.
-   **Gestión de Empleados**: Altas, bajas y gestión completa de perfiles de empleados.
-   **Integración Biométrica**: Conexión nativa con dispositivos ZKTeco (relojes checadores) para sincronización automática de registros.
-   **Reportes Detallados**: Generación de reportes de asistencia, retardos, horas extra y más (exportables a Excel/PDF).
-   **Turnos y Horarios**: Configuración flexible de turnos laborales.
-   **Procesos en Segundo Plano**: Uso de Hangfire para tareas programadas (descarga automática de logs, cálculo de asistencias).
-   **Migración Automática**: El sistema verifica y actualiza la estructura de la base de datos automáticamente al iniciar.

## 🛠️ Tecnologías

*   **Core**: .NET 9.0 (C#)
*   **Frontend**: Blazor Server con [MudBlazor](https://mudblazor.com/)
*   **Base de Datos**: PostgreSQL
*   **ORM**: Entity Framework Core 9 (Npgsql)
*   **Background Jobs**: Hangfire
*   **Manejo de Logs**: Serilog (con sink a PostgreSQL y Archivos)
*   **Integración Hardware**: ZKTeco SDK (Standalone SDK)
*   **Arquitectura**: Clean Architecture + CQRS (MediatR)

## 🏗️ Arquitectura del Proyecto

El proyecto sigue una estructura modular estricta:

*   **`Core/`**:
    *   `AttendanceSystem.Domain`: Reglas de negocio puras, entidades y eventos.
    *   `AttendanceSystem.Application`: Casos de uso implementados con patrón CQRS.
*   **`Infrastructure/`**:
    *   `AttendanceSystem.Infrastructure`: Implementación de persistencia y servicios externos.
    *   `AttendanceSystem.ZKTeco`: Librería de integración directa con el SDK nativo.
*   **`Presentation/`**:
    *   `AttendanceSystem.Blazor.Server`: Aplicación web principal.
    *   `AttendanceSystem.ZKTeco.Service`: Servicio Windows gRPC (x86) para comunicar con el hardware (necesario por dependencias de 32-bits del SDK).

## 📋 Prerrequisitos

*   [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
*   [PostgreSQL](https://www.postgresql.org/download/) (versión 14 o superior recomendada)
*   Sistema Operativo Windows (Requerido para el servicio ZKTeco debido a DLLs nativas)

## ⚙️ Instalación y Configuración

1.  **Clonar el repositorio**
    ```powershell
    git clone https://github.com/Aletsis/AttendanceSystem.git
    cd AttendanceSystem
    ```

2.  **Configurar Base de Datos**
    *   Asegúrate de que el servicio de PostgreSQL esté corriendo.
    *   Crea una base de datos llamada `AttendanceSystem` (o el nombre que prefieras).

3.  **Configurar Aplicación**
    *   Ve a la carpeta del proyecto web:
        ```powershell
        cd src/Presentation/AttendanceSystem.Blazor.Server
        ```
    *   Crea tu archivo de configuración basado en el ejemplo:
        ```powershell
        copy appsettings.example.json appsettings.json
        ```
    *   Edita `appsettings.json` y coloca tus credenciales de PostgreSQL en `ConnectionStrings`:
        ```json
        "ConnectionStrings": {
          "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=TU_PASSWORD;",
          "HangfireDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=TU_PASSWORD;"
        }
        ```

## ▶️ Ejecución

Para ejecutar el sistema completo necesitas correr dos componentes:

### 1. Aplicación Web (Blazor)
Esta es la interfaz principal. Al iniciar, aplicará automáticamente las migraciones necesarias a la base de datos.
```powershell
# En una terminal
cd src/Presentation/AttendanceSystem.Blazor.Server
dotnet run
```
Accede a `https://localhost:7168` (o el puerto indicado en la consola).

### 2. Servicio ZKTeco
Este servicio puente permite la comunicación con los relojes checadores (requiere arquitectura x86).
```powershell
# En otra terminal
cd src/Presentation/AttendanceSystem.ZKTeco.Service
dotnet run
```
*Nota: Si no necesitas conectar dispositivos físicos inmediatamente, puedes usar solo la aplicación web.*

## 📄 Notas de Migración
Si vienes de versiones anteriores que usaban SQL Server, consulta [MIGRACION_POSTGRESQL.md](MIGRACION_POSTGRESQL.md) para detalles sobre los cambios realizados.

## 🤝 Contribución
Las Pull Requests son bienvenidas. Para cambios mayores, por favor abre primero un issue para discutir lo que te gustaría cambiar.

## 📄 Licencia
Este proyecto es privado y confidencial.
