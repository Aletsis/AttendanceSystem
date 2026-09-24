# 📚 Centro de Documentación - Attendance System

Bienvenido al centro de documentación técnica y operativa de **Attendance System**. Aquí encontrarás manuales, especificaciones de arquitectura, guías de despliegue, análisis de integraciones y scripts de soporte.

---

## 📑 Índice General de Documentos

### 🏛️ Arquitectura e Infraestructura
* 📘 [**MANUAL_TECNICO.md**](MANUAL_TECNICO.md) - Manual técnico exhaustivo para desarrolladores y administradores de sistemas (requisitos de hardware, instalación Inno Setup, esquemas de BD, protocolos y endpoints).
* 🏛️ [**ARCHITECTURE.md**](ARCHITECTURE.md) - Explicación detallada de la arquitectura de la solución, Clean Architecture y la separación por gRPC (Blazor x64 $\leftrightarrow$ Servicio Windows x86).
* 🔌 [**GRACEFUL_SHUTDOWN_RESUMEN.md**](GRACEFUL_SHUTDOWN_RESUMEN.md) - Mecanismos de apagado ordenado (*Graceful Shutdown*), liberación de recursos y manejo de señales en los servicios.

### 🚀 Despliegue y Mantenimiento
* 🚀 [**DEPLOYMENT_GUIDE.md**](DEPLOYMENT_GUIDE.md) - Guía completa paso a paso para el despliegue en entornos de producción (Instalador Todo-en-Uno, modo Self-Hosted Kestrel e IIS).
* 🔄 [**REINSTALL_SERVICE.md**](REINSTALL_SERVICE.md) - Guía paso a paso para detener, desinstalar y reinstalar limpiamente el servicio Windows de ZKTeco.
* 📦 [**README_INSTALLER.md**](../Deploy/README_INSTALLER.md) - Instrucciones de compilación y empaquetado del instalador desatendido con Inno Setup.

### 👥 Operación y Gestión
* 📗 [**MANUAL_OPERACIONES.md**](MANUAL_OPERACIONES.md) - Manual de usuario práctico para personal de Recursos Humanos, supervisores de área y operadores de nómina.
* 🔐 [**AUTHENTICATION.md**](AUTHENTICATION.md) - Guía de autenticación y autorización con ASP.NET Core Identity, roles, políticas de contraseñas y sesiones.

### 🖲️ Integraciones de Hardware
* 📹 [**HIKVISION_INTEGRATION_ANALYSIS.md**](HIKVISION_INTEGRATION_ANALYSIS.md) - Análisis técnico, endpoints ISAPI y arquitectura de comunicación con terminales Hikvision.
* 📝 [**LOGGING.md**](LOGGING.md) - Estrategia integral de logging estructurado con Serilog (sinks a PostgreSQL, archivos rotativos y visor de eventos).
* ⚙️ [**CICD_GUIDE.md**](CICD_GUIDE.md) - Documentación sobre flujos de Integración y Entrega Continua con GitHub Actions.

---

## 🔧 Herramientas y Scripts de Automatización

### 🔍 [Diagnose-AttendanceSystem.ps1](Diagnose-AttendanceSystem.ps1)
**Script de Diagnóstico Automatizado**
Verifica el estado del servicio Windows, puerto gRPC (5001), puerto Web (8081/80), conectividad a PostgreSQL (5432), registro de librerías COM (`zkemkeeper.dll`), reglas de firewall y eventos recientes.

**Ejecutar:**
```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
.\Docs\Diagnose-AttendanceSystem.ps1
```

---

### 🔄 [Reinstall-ZKTecoService.ps1](Reinstall-ZKTecoService.ps1)
**Script de Reinstalación del Servicio ZKTeco**
Detiene el servicio actual, elimina el registro previo en Windows, copia y registra las DLLs de 32 bits en `SysWOW64`, recrea el servicio e inicia la validación en el puerto 5001.

**Ejecutar** (en PowerShell como Administrador):
```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
.\Docs\Reinstall-ZKTecoService.ps1
```

---

### 📄 Plantillas de Configuración
* [**appsettings.Production.json.template**](appsettings.Production.json.template) - Plantilla base para configuración del servidor Blazor en producción.
* [**appsettings.ZKTecoService.json.template**](appsettings.ZKTecoService.json.template) - Plantilla base para el servicio de Windows de ZKTeco.

---

## 🚀 Guía Rápida de Inicio

```
┌────────────────────────────────────────────────────────┐
│ 1. Consulta la Arquitectura: Docs/ARCHITECTURE.md      │
│ 2. Sigue el Despliegue:     Docs/DEPLOYMENT_GUIDE.md   │
│ 3. Diagnostica el Entorno:   Docs/Diagnose-Attendance...│
└────────────────────────────────────────────────────────┘
```

### Checklist Rápido de Validación:
- [ ] PostgreSQL 16 instalado y con base de datos `AttendanceSystem` creada.
- [ ] Servicio Windows `AttendanceSystem.ZKTeco.Service` en estado `Running`.
- [ ] Puerto local `5001` (gRPC) y puerto `8081` / `80` (Web) respondiendo.
- [ ] Script de diagnóstico `Diagnose-AttendanceSystem.ps1` finalizado sin errores críticos.
- [ ] Inicio de sesión exitoso en la web con usuario `admin` y contraseña `Admin123!`.

---

## 📞 Soporte y Referencia

* **Desarrollador / Organización:** Aletsis
* **Repositorio:** [https://github.com/Aletsis/AttendanceSystem](https://github.com/Aletsis/AttendanceSystem)
* **Versión del Sistema:** `2.1.6` (.NET 10.0 / PostgreSQL 16.x)

