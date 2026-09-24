# AttendanceSystem - Sistema Integral de Control de Asistencia y Gestión Biométrica

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16.x-336791?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![MudBlazor](https://img.shields.io/badge/MudBlazor-9.7-0099FF?logo=blazor&logoColor=white)](https://mudblazor.com/)
[![Clean Architecture](https://img.shields.io/badge/Architecture-Clean%20Architecture%20%2B%20DDD%20%2B%20CQRS-brightgreen)](Docs/ARCHITECTURE.md)
[![Version](https://img.shields.io/badge/version-2.1.6-blue)](Directory.Build.props)

Sistema empresarial moderno de control de asistencia de personal desarrollado en **.NET 10** con **Blazor Server** (MudBlazor), cliente de escritorio **WPF**, integración nativa con dispositivos biométricos (**ZKTeco** y **Hikvision**) y persistencia en **PostgreSQL**. Diseñado rigurosamente bajo los principios de **Clean Architecture**, **Domain-Driven Design (DDD)** y el patrón **CQRS con MediatR**.

---

## 🚀 Características Principales

- 📊 **Dashboard Ejecutivo en Tiempo Real**: Monitoreo dinámico de métricas de puntualidad, empleados presentes, retardos, ausencias, gráficos estadísticos y estado de conexión de relojes checadores.
- 🏢 **Gestión Organizacional Completa**: Catálogos jerárquicos de Sucursales, Departamentos, Puestos y Empleados, incluyendo herramientas de importación/exportación masiva mediante Excel/CSV.
- 🕒 **Control Flexible de Horarios y Turnos**: Definición de jornadas laborales, horarios matutinos, vespertinos y nocturnos, tolerancias de entrada/salida y asignación de días de descanso.
- 🖲️ **Integración Biométrica Multi-Marca**:
  - **ZKTeco**: Comunicación nativa mediante SDK Standalone x86 (COM/DLL) para sincronización de huellas, rostros, tarjetas y logs de asistencia vía servicio bridge gRPC.
  - **Hikvision**: Comunicación directa mediante protocolo HTTP/REST (ISAPI) y eventos ACS.
  - Sincronización automática de hora, prueba de conectividad y gestión remota de usuarios en terminales.
- 📑 **Reportes y Análisis Avanzados**:
  - Reportes de asistencia detallada y acumulada con filtros por período, sucursal, departamento y empleado.
  - Módulos especializados: **Análisis de Retardos** (*Tardiness Analysis*), **Análisis de Ausentismo** (*Absenteeism Analysis*) y **Tarjetas de Asistencia** (*Attendance Cards*).
  - Exportación profesional a **Excel** (ClosedXML) y **PDF** (QuestPDF).
- ⚙️ **Cálculo y Consolidación de Asistencia**:
  - Procesamiento inteligente de marcajes crudos a asistencias diarias con detección automática de retardos, salidas anticipadas y horas extras.
  - Módulo de **Checadas Manuales** (*Manual Logs*) con justificación para corrección de incidencias.
- ⚡ **Automatización y Tareas en Segundo Plano**:
  - Motor de tareas programadas con **Hangfire** respaldado en PostgreSQL.
  - Descarga automática periódica de checadas desde dispositivos.
  - Cálculo nocturno de asistencias y envío programado de reportes por correo electrónico vía **SendGrid**.
- 🔐 **Seguridad y Autenticación**:
  - Autenticación robusta basada en **ASP.NET Core Identity** con cookies seguras y control de roles (*Administrador*, *Operador de RH*, *Supervisor*).
  - Políticas de seguridad para contraseñas y revalidación periódica de sesiones.
- 💾 **Respaldos y Mantenimiento**: Módulo integrado de copias de seguridad de base de datos (`pg_dump` y `pg_restore`), configuración de rutas y gestión de archivos de respaldo.
- 📝 **Auditoría y Diagnóstico**: Registro estructurado con **Serilog** (con sinks hacia PostgreSQL, archivos rotativos y consola).

---

## 🛠️ Stack Tecnológico

| Capa / Componente | Tecnología / Librería | Descripción |
| :--- | :--- | :--- |
| **Framework Core** | .NET 10.0 (C# 13) | Plataforma de desarrollo principal de alto rendimiento |
| **Frontend Web** | Blazor Server + MudBlazor 9.7 | Interfaz de usuario interactiva, moderna y responsiva |
| **Frontend Desktop** | WPF + Prism 8 (MVVM) | Cliente de escritorio alternativo con integración MediatR |
| **Base de Datos** | PostgreSQL 16.x | Motor relacional robusto y escalable |
| **ORM & Migraciones** | Entity Framework Core 10 (Npgsql) | Mapeo objeto-relacional y migraciones automáticas de esquema |
| **Arquitectura / CQRS**| MediatR 14.2 + FluentValidation 12 | Desacoplamiento de casos de uso y validación de comandos |
| **Comunicación RPC** | gRPC (HTTP/2) + Google Protobuf | Puente de comunicación de alto rendimiento entre Blazor (x64) y SDK (x86) |
| **Background Jobs** | Hangfire 1.8 + Hangfire.PostgreSQL | Orquestación y ejecución de tareas programadas en segundo plano |
| **Generación Documental**| QuestPDF & ClosedXML | Generación de reportes PDF vectoriales y hojas de cálculo Excel |
| **Mailing** | SendGrid | Servicio de entrega de notificaciones y reportes automatizados |
| **Logging** | Serilog 10 (PostgreSQL, File, Console)| Telemetría estructurada con enriquecedores de entorno |
| **Testing** | xUnit, Moq, Bunit & Coverlet | Pruebas unitarias de Dominio, Aplicación, Infraestructura y Blazor |
| **Despliegue** | Inno Setup 6 & PowerShell | Instalador Todo-en-Uno desatendido con soporte Self-Hosted e IIS |

---

## 🏗️ Arquitectura de la Solución

El proyecto sigue una estructura modular estricta basada en **Clean Architecture**:

```
AttendanceSystem/
├── src/
│   ├── Core/
│   │   ├── AttendanceSystem.Domain/         # Entidades, Value Objects, Enums, Eventos y Reglas de Negocio
│   │   └── AttendanceSystem.Application/    # Casos de uso (Commands/Queries), DTOs, Validadores e Interfaces
│   ├── Infrastructure/
│   │   ├── AttendanceSystem.Infrastructure/ # EF Core, PostgreSQL, Repositorios, Hangfire, Mailing, Reportes
│   │   └── AttendanceSystem.ZKTeco/         # Adaptador nativo ZKTeco SDK (COM Interop x86)
│   └── Presentation/
│       ├── AttendanceSystem.Blazor.Server/  # Aplicación Web principal (x64) con MudBlazor
│       ├── AttendanceSystem.WPF/            # Aplicación de escritorio (MVVM con Prism y MediatR)
│       └── AttendanceSystem.ZKTeco.Service/ # Servicio Windows gRPC (x86) para comunicar con el hardware
├── tests/
│   ├── AttendanceSystem.Domain.UnitTests/         # Pruebas unitarias del modelo de dominio
│   ├── AttendanceSystem.Application.UnitTests/    # Pruebas unitarias de casos de uso CQRS
│   ├── AttendanceSystem.Infrastructure.Tests/     # Pruebas de integración de persistencia y servicios
│   └── AttendanceSystem.Blazor.UnitTests/         # Pruebas de componentes UI con Bunit
├── Deploy/                                  # Scripts de automatización, base de datos y script Inno Setup
└── Docs/                                    # Manuales técnicos, de operaciones, guías de despliegue y CI/CD
```

### ¿Por qué existe un servicio gRPC separado?

Los relojes checadores ZKTeco requieren librerías dinámicas nativas (`zkemkeeper.dll`) que operan exclusivamente en **arquitectura de 32 bits (x86)**. Para permitir que la aplicación web principal se ejecute en **64 bits (x64)** con el máximo rendimiento y escalabilidad, se utiliza `AttendanceSystem.ZKTeco.Service` como un bridge stateless vía **gRPC (HTTP/2 en puerto 5001)**.

```
┌─────────────────────────────────┐
│ AttendanceSystem.Blazor.Server  │ (x64 Web UI)
│   - Casos de Uso / CQRS         │
│   - PostgreSQL 16 (EF Core)     │
│   - Hangfire Jobs               │
└───────────────┬─────────────────┘
                │ gRPC (Puerto 5001)
                ▼
┌─────────────────────────────────┐
│ AttendanceSystem.ZKTeco.Service │ (x86 Windows Service)
│   - Adaptador SDK ZKTeco        │
└───────────────┬─────────────────┘
                │ TCP/UDP (Puerto 4370)
                ▼
┌─────────────────────────────────┐
│ Relojes Checadores (ZKTeco)     │
└─────────────────────────────────┘
```

---

## 📋 Prerrequisitos de Desarrollo

* [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* [PostgreSQL 16](https://www.postgresql.org/download/) (o superior)
* Sistema Operativo:
  * **Windows 10/11 Pro** o **Windows Server 2019/2022** (Requerido para compilar y ejecutar el servicio x86 de ZKTeco y la aplicación WPF).
  * Linux / macOS soportado para desarrollo y ejecución del núcleo Web (Blazor) contra dispositivos virtuales o Hikvision ISAPI.

---

## ⚙️ Instalación y Configuración para Desarrollo

### 1. Clonar el repositorio
```bash
git clone https://github.com/Aletsis/AttendanceSystem.git
cd AttendanceSystem
```

### 2. Configurar la Base de Datos
Asegúrate de que PostgreSQL esté en ejecución y crea una base de datos:
```sql
CREATE DATABASE "AttendanceSystem";
```

### 3. Configurar Cadenas de Conexión
Crea o edita el archivo de configuración en `src/Presentation/AttendanceSystem.Blazor.Server/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=TU_PASSWORD;",
    "HangfireDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=TU_PASSWORD;"
  },
  "ZKTecoService": {
    "Url": "http://localhost:5001"
  }
}
```

---

## ▶️ Ejecución de los Proyectos

Para contar con la funcionalidad completa en desarrollo, ejecuta los servicios correspondientes:

### 1. Aplicación Web Principal (Blazor)
Aplica automáticamente las migraciones pendientes en PostgreSQL y levanta la interfaz web:
```bash
dotnet run --project src/Presentation/AttendanceSystem.Blazor.Server
```
* **Acceso**: Abre en tu navegador `https://localhost:7168` o `http://localhost:5247`
* **Credenciales por defecto**:
  * **Usuario**: `admin`
  * **Contraseña**: `Admin123!`

### 2. Servicio Puente ZKTeco (gRPC x86)
*(Necesario únicamente cuando se requiera interactuar con checadores ZKTeco reales o emulados)*:
```powershell
dotnet run --project src/Presentation/AttendanceSystem.ZKTeco.Service
```
* Escucha por defecto en `http://localhost:5001`.

### 3. Cliente de Escritorio WPF (Opcional)
```powershell
dotnet run --project src/Presentation/AttendanceSystem.WPF
```

---

## 🧪 Pruebas Unitarias y Calidad

Para ejecutar la suite completa de pruebas unitarias y de integración:

```bash
# Ejecutar todas las pruebas
dotnet test AttendanceSystem.sln

# Ejecutar pruebas con reporte de cobertura
dotnet test --collect:"XPlat Code Coverage"
```

El proyecto cuenta con integración continua (**GitHub Actions**) en `.github/workflows/` para compilación automatizada, verificación de formato y ejecución de pruebas con cobertura en cada push y pull request.

---

## 📦 Generación del Instalador para Producción

El proyecto incluye un instalador "Todo en Uno" compilado con **Inno Setup**:

1. Compilar y empaquetar los artefactos autocontenidos:
   ```powershell
   .\Prepare-Release.ps1
   ```
2. Compilar el script de instalación en `Deploy\setup_script.iss` con Inno Setup Compiler.
3. El instalador resultante (`AttendanceSystem_Setup.exe`) automatiza:
   - Instalación desatendida de PostgreSQL y .NET Hosting Bundle.
   - Registro de librerías COM de ZKTeco en Windows.
   - Creación del Servicio de Windows `AttendanceSystem.ZKTeco.Service`.
   - Inicialización del esquema de base de datos y apertura de puertos en Firewall.
   - Opción de despliegue Self-Hosted (puerto 8081) o en IIS (puerto 80).

Consulta [Deploy/README_INSTALLER.md](Deploy/README_INSTALLER.md) para más detalles.

---

## 📚 Documentación Adicional

Para más detalles técnicos y operativos, consulta los documentos especializados en la carpeta `Docs/`:

* 📘 [Manual Técnico](Docs/MANUAL_TECNICO.md) - Arquitectura a fondo, flujos de datos, protocolos y esquemas.
* 📗 [Manual de Operaciones](Docs/MANUAL_OPERACIONES.md) - Guía para administradores de RH, supervisores y operadores.
* 🏛️ [Arquitectura del Sistema](Docs/ARCHITECTURE.md) - Detalle de separación x86/x64 y comunicación gRPC.
* 🔐 [Guía de Autenticación](Docs/AUTHENTICATION.md) - Gestión de usuarios, roles y sesiones con ASP.NET Identity.
* 📝 [Estrategia de Logging](Docs/LOGGING.md) - Configuración y enriquecimiento de Serilog.
* 🚀 [Guía de CI/CD](Docs/CICD_GUIDE.md) - Workflows de GitHub Actions y versionado de releases.
* 📹 [Integración Hikvision](Docs/HIKVISION_INTEGRATION_ANALYSIS.md) - Análisis e integración del protocolo ISAPI.
* 🔌 [Cierre Elegante](Docs/GRACEFUL_SHUTDOWN_RESUMEN.md) - Manejo de señales y graceful shutdown.

---

## 🤝 Contribución

1. Haz un Fork del proyecto.
2. Crea una rama para tu feature (`git checkout -b feature/NuevaCaracteristica`).
3. Confirma tus cambios (`git commit -m 'feat: Agrega nueva caracteristica'`).
4. Haz push a la rama (`git push origin feature/NuevaCaracteristica`).
5. Abre un **Pull Request**.

---

## 📄 Licencia

Este proyecto es de carácter privado y confidencial. Prohibida su copia o distribución no autorizada.
