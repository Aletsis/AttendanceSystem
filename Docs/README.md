# 📚 Centro de Documentación - Attendance System

Bienvenido al centro de documentación técnica y operativa de **Attendance System**. Aquí encontrarás manuales, especificaciones de arquitectura, guías de despliegue, análisis de integraciones y scripts de soporte.

---

## ✅ Documentación verificada (en construcción)

La documentación se está reescribiendo por fases. Cada afirmación de estos documentos cita el archivo y la línea del
código que la respalda, y se puede comprobar con las consultas y scripts que incluyen.

| Documento | Contenido |
|---|---|
| [tecnico/01-arquitectura.md](tecnico/01-arquitectura.md) | Proyectos, procesos, formas de instalación, puertos, recorrido de una checada, tareas en segundo plano, arranque y mantenimiento |
| [tecnico/02-base-de-datos.md](tecnico/02-base-de-datos.md) | Esquemas, convenciones, relaciones e integridad, tablas por área, enumeraciones, migraciones, bloqueos, problemas y consultas de verificación |
| [tecnico/02b-esquema-columnas.md](tecnico/02b-esquema-columnas.md) | Detalle columna por columna, generado desde el modelo de EF Core |
| [tecnico/03-motor-de-calculo.md](tecnico/03-motor-de-calculo.md) | Cómo se calcula cada jornada: horario del día, marcajes, fórmulas de retardo, trabajado, comida, salidas y horas extra, y 68 ejemplos numéricos por tipo de turno que son pruebas automáticas |
| [tecnico/04-dispositivos.md](tecnico/04-dispositivos.md) | Relojes: servicio ZKTeco por SDK, protocolo ADMS mensaje por mensaje, cola de comandos, Hikvision (experimental), zona horaria, resincronización y cuarentena de marcajes |
| [tecnico/05-instalacion-configuracion.md](tecnico/05-instalacion-configuracion.md) | Instalador paso a paso, instalación manual, equipo de desarrollo, cada clave de `appsettings` y la configuración guardada en la base |
| [tecnico/06-operacion.md](tecnico/06-operacion.md) | Respaldo y restauración, mantenimiento, usuarios, roles y permisos, bitácoras, panel de tareas, alertas, scripts de diagnóstico e índice de síntomas |
| [tecnico/07-limitaciones.md](tecnico/07-limitaciones.md) | Lista de seguimiento: 24 defectos, 19 limitaciones de diseño (incluida la revisión de seguridad) y 8 reglas de cálculo por confirmar, cada uno con gravedad, cómo comprobarlo, solución temporal y corrección recomendada |
| [manual-rh/manual-rh.md](manual-rh/manual-rh.md) | Manual de Recursos Humanos por tareas, en el sitio y en el programa de escritorio: empleados, horarios, checadas, revisión del día, periodos, reportes y nómina, permisos por rol y reglas de cálculo en revisión. Capturas pendientes en [manual-rh/capturas.md](manual-rh/capturas.md) |

Herramientas: `python Docs/tools/generar_esquema.py [--check]` regenera (o comprueba) el detalle de columnas;
`python Docs/tools/verificar_referencias.py` comprueba que las citas al código existan;
`dotnet test tests/AttendanceSystem.Application.UnitTests --filter "FullyQualifiedName~MotorCalculoEjemplosTests"` ejecuta los ejemplos del motor de cálculo;
`DispositivosEjemplosTests` (mismo proyecto) y `AdmsStampDocumentacionTests` (`tests/AttendanceSystem.Blazor.UnitTests`)
fijan los comportamientos de los relojes que cita el documento 04;
`InstalacionDocumentacionTests` (`tests/AttendanceSystem.Infrastructure.Tests`) los de la configuración del instalador (documento 05).

> ⚠️ Los documentos del índice siguiente son anteriores a esta revisión y **pueden no coincidir con el código**
> (por ejemplo, la versión y la contraseña inicial del checklist de abajo ya no son correctas: el usuario `admin` se
> crea con `InitialAdmin:Password` o una contraseña generada, ver
> [arquitectura, sección 9](tecnico/01-arquitectura.md#9-arranque-y-modo-mantenimiento)). Se reemplazarán en las
> siguientes fases.

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
Sólo lee. Revisa el servicio ZKTeco y su `ApiKey`, el puerto gRPC, el registro COM de 32 bits del SDK, el sitio en IIS, la configuración del programa de escritorio, los puertos ADMS y de PostgreSQL, las reglas de firewall y los últimos errores de cada bitácora. Detalle en [tecnico/06-operacion.md](tecnico/06-operacion.md#41-scripts).

**Ejecutar:**
```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
.\Docs\Diagnose-AttendanceSystem.ps1
```

---

### 🔄 [Reinstall-ZKTecoService.ps1](Reinstall-ZKTecoService.ps1)
**Script de Reinstalación del Servicio ZKTeco**
Hace lo mismo que el instalador: detiene y elimina el servicio, registra `zkemkeeper.dll` con el `regsvr32` de 32 bits desde la carpeta del servicio (sin copiar DLL a `SysWOW64`), lo recrea, lo inicia y comprueba su puerto gRPC.

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

