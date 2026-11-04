# 06 · Operación y solución de problemas

Este documento es para soporte y TI: cómo respaldar y restaurar, qué hacer cuando el sistema queda en mantenimiento,
cómo administrar usuarios y accesos, y dónde buscar cuando algo falla. Cada afirmación cita el archivo y la línea que la
respalda. Los procedimientos que no se probaron lo dicen.

Documentos relacionados: [arquitectura](01-arquitectura.md), [base de datos](02-base-de-datos.md) (problemas de
migraciones y consultas de verificación), [relojes](04-dispositivos.md) y
[instalación y configuración](05-instalacion-configuracion.md).

Índice

1. [Respaldo y restauración](#1-respaldo-y-restauración)
2. [Arranque, actualización y mantenimiento](#2-arranque-actualización-y-mantenimiento)
3. [Usuarios y accesos](#3-usuarios-y-accesos)
4. [Diagnóstico](#4-diagnóstico)
5. [Índice de síntomas](#5-índice-de-síntomas)
6. [Comportamientos verificados que conviene revisar](#6-comportamientos-verificados-que-conviene-revisar)

---

## 1. Respaldo y restauración

Todo se hace desde la pantalla de respaldos del sitio o del programa de escritorio y exige el permiso `Backups.Manage`
([Backup.razor:2](../../src/Presentation/AttendanceSystem.Blazor.Server/Components/Pages/Backup.razor#L2),
[CreateBackupCommand.cs:6](../../src/Core/AttendanceSystem.Application/Features/Backup/Commands/CreateBackupCommand.cs#L6),
[RestoreBackupCommand.cs:6](../../src/Core/AttendanceSystem.Application/Features/Backup/Commands/RestoreBackupCommand.cs#L6)).
Sólo el rol Administrador lo tiene (sección 3.1).

### 1.1 Tipos de archivo

| Nombre | Qué es | Cuándo se crea | Retención | Código |
|---|---|---|---|---|
| `Full_Backup_AAAAMMDD_hhmmss.zip` | `database.backup` (volcado de la base), `config/appsettings.json` y `metadata.json` | A mano ("completo") y en el respaldo automático | Sí | [BackupService.cs:137-288](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L137-L288) |
| `DB_Backup_AAAAMMDD_hhmmss.backup` | Sólo el volcado | A mano ("sólo base") | Sí | [líneas 342-401](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L342-L401) |
| `PreMigration_Backup_AAAAMMDD_hhmmss.backup` | Volcado antes de aplicar migraciones | Al arrancar una versión nueva con migraciones pendientes | No | [líneas 305-340](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L305-L340), [StartupMigrationService.cs:112-124](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/StartupMigrationService.cs#L112-L124) |
| `.safety_snapshot_AAAAMMDD_hhmmss.backup` | Copia de la base justo antes de restaurar | En cada restauración; se borra si termina bien o si no llegó a cambiar la base | No | [líneas 517-556](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L517-L556) |
| Cualquier `.zip`, `.backup` o `.bak` subido | Respaldo traído de otro equipo | Al subirlo | No | [líneas 921-1021](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L921-L1021) |

Si dos respaldos caen en el mismo segundo, el segundo lleva un consecutivo en vez de reemplazar al primero
([líneas 403-413](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L403-L413)).

**Qué contiene el volcado.** `pg_dump` en formato personalizado (`-F c`), **sin** los esquemas `system_maintenance`
(estado de mantenimiento) ni `hangfire` (cola de tareas) y sin las tablas `logs` y `Logs`
([BackupService.cs:1292-1297](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1292-L1297),
[líneas 1445-1453](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1445-L1453)).
Antes de quedar en la lista, cada respaldo se verifica con `pg_restore -l`; uno que no pasa se borra
([líneas 175-182](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L175-L182)).

### 1.2 Dónde se guardan

En `SystemConfiguration.BackupDirectory`; si es una ruta relativa, se toma desde la carpeta del programa que hace el
respaldo ([BackupService.cs:93-135](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L93-L135)).
Antes de escribir se comprueba que se pueda escribir en la carpeta y que haya al menos 100 MB libres
([líneas 1168-1214](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1168-L1214)).
Eliminar o restaurar sólo acepta archivos que estén directamente en esa carpeta
([líneas 1080-1103](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1080-L1103)).

`pg_dump` y `pg_restore` se buscan en `Backup:PostgresBinPath` ([instalación, 10.1](05-instalacion-configuracion.md#101-sitio-web-y-programa-de-escritorio)).

### 1.3 Respaldo automático y retención

- **Automático.** Si está activado en la configuración, todos los días a la hora indicada se hace un respaldo
  **completo** ([HangfireAttendanceJobScheduler.cs:440-453](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/HangfireAttendanceJobScheduler.cs#L440-L453)).
  Si falla, se reintenta 2 veces, a los 10 y a los 30 minutos
  ([línea 204](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/HangfireAttendanceJobScheduler.cs#L204),
  [línea 440](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/HangfireAttendanceJobScheduler.cs#L440)).
- **Retención.** Después de cada respaldo se conservan los `Backup:KeepLastNBackups` (10) más recientes, contando
  juntos los completos y los de sólo base. Los subidos, los previos a migrar y los snapshots no se borran nunca solos
  ([BackupService.cs:1105-1160](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1105-L1160),
  [líneas 752-766](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L752-L766)).
  Con 0 o menos, no se borra ninguno.
- **Copia fuera del equipo.** El sistema no la hace: hay que copiar la carpeta de respaldos a otro lugar.

### 1.4 Restauración

[BackupService.cs:415-647](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L415-L647).
Durante toda la restauración el sistema está en mantenimiento: se rechazan las escrituras, los relojes ADMS reciben
`503` y las tareas se pausan ([arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento)).

| # | Paso | Si falla | Código |
|---|---|---|---|
| 1 | Tomar el candado de mantenimiento (uno solo en todos los procesos) | Se rechaza: ya hay otra restauración | [líneas 448-458](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L448-L458) |
| 2 | Si es `.zip`, extraerlo y buscar `database.backup` | Se cancela; la base no cambia | [líneas 463-481](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L463-L481) |
| 3 | Validar el archivo con `pg_restore -l` | Se cancela; la base no cambia | [líneas 483-491](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L483-L491) |
| 4 | Esperar a que terminen las escrituras en curso (hasta `Backup:DrainTimeoutSeconds`, 60 s) y pausar las tareas | Se cancela; la base no cambia | [líneas 493-515](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L493-L515) |
| 5 | Snapshot de seguridad de la base actual, verificado | Se cancela; la base no cambia | [líneas 517-556](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L517-L556) |
| 6 | Restaurar en **una sola transacción**: borra y recrea `public` y carga el respaldo | Sin `COMMIT`, la base no cambia. Con un `COMMIT` incierto, vuelve al snapshot | [líneas 558-580](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L558-L580), [1458-1466](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1458-L1466) |
| 7 | Comprobar que existan las tablas principales | Vuelve al snapshot | [líneas 582-592](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L582-L592) |
| 8 | Alinear el esquema con esta versión: un respaldo de una versión anterior se migra; uno de una versión más nueva se rechaza | Vuelve al snapshot | [líneas 594-601](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L594-L601), [1945-1985](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1945-L1985) |
| 9 | Borrar el snapshot, soltar el candado y reanudar las tareas, con la programación de la configuración restaurada | — | [líneas 603-619](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L603-L619) |

El mensaje final pide **reiniciar la aplicación** ([línea 617](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L617)):
reciclar el grupo de aplicaciones en IIS y cerrar y abrir el programa de escritorio (sección 2.3).

La configuración que viene en un `.zip` (`config/appsettings.json`) **no se aplica**: la carpeta temporal se borra al
terminar ([líneas 639-646](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L639-L646)).
Si hace falta, hay que abrir el `.zip` y copiar la configuración a mano.

### 1.5 Si la restauración falla

| Mensaje | Qué pasó | Qué hacer |
|---|---|---|
| "… La base de datos no se modificó." | Falló antes del `COMMIT` | Corregir la causa (archivo dañado, espacio, permisos) y repetir |
| "… La base de datos fue revertida exitosamente a su estado original previo." | Falló después y el snapshot se restauró bien | Igual que el anterior |
| "FALLO CRÍTICO: … el rollback automático también falló … Snapshot previo conservado en: …" | Ni la restauración ni la reversión terminaron | El sistema queda en mantenimiento (`Failed`), **también tras reiniciar**, hasta que una restauración termine bien. Restaurar el snapshot que indica el mensaje, o cualquier otro respaldo. El snapshot aparece en la lista de respaldos |

Código: [BackupService.cs:649-728](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L649-L728),
[línea 803-808](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L803-L808).

---

## 2. Arranque, actualización y mantenimiento

Cómo decide el arranque si migra, siembra o entra en mantenimiento está en
[arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento). Los problemas de migraciones
(bloqueos, tiempo agotado, migraciones desordenadas) están en [base de datos, sección 9](02-base-de-datos.md#9-problemas-posibles-y-cómo-resolverlos).
Aquí, lo que necesita soporte.

### 2.1 Estados de mantenimiento

Se guardan en la fila 1 de `system_maintenance.maintenance_lock`, con el nombre del estado en texto
([RestoreStateService.cs:476-500](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/RestoreStateService.cs#L476-L500),
[MaintenanceState.cs:6-42](../../src/Core/AttendanceSystem.Application/Common/MaintenanceState.cs#L6-L42)):

| Estado | Significa |
|---|---|
| `Normal` | Operación normal |
| `Preparing`, `Draining`, `Restoring` | Restauración en curso, antes del `COMMIT` |
| `Validating`, `Recovering` | Restauración en curso, después del `COMMIT` |
| `Failed` | Fallo de arranque o de restauración: sólo lectura |

Consulta (sólo lectura):

```sql
SELECT state, owner_id, updated_at, details FROM system_maintenance.maintenance_lock WHERE id = 1;
```

### 2.2 Cómo se sale de cada caso

Lo decide `LoadPersistedStateAsync` al arrancar cada proceso
([RestoreStateService.cs:70-123](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/RestoreStateService.cs#L70-L123)):

| Situación | `owner_id` | Al reiniciar | Qué hacer |
|---|---|---|---|
| El arranque no pudo migrar (falló el respaldo previo, una migración, o la base es de una versión más nueva) | El del arranque | Se vuelve a intentar | Corregir la causa que dice `details` (y la bitácora) y reiniciar. Si la base es de una versión más nueva, instalar esa versión |
| Una restauración se interrumpió antes del `COMMIT` (el proceso murió) | El de la restauración | Vuelve a `Normal`: la base no cambió | Nada |
| Una restauración se interrumpió después del `COMMIT`, o falló su reversión | El de la restauración | Sigue en `Failed` | Restaurar un respaldo desde la pantalla de respaldos |
| Otro proceso está restaurando ahora | El de ese proceso | Espera a que termine | Esperar |

Si una migración falla a medias, el respaldo previo (`PreMigration_Backup_…`) queda en la carpeta de respaldos para
restaurarlo. **No hay** un botón ni un comando para forzar la salida de `Failed`.

### 2.3 Reiniciar cada parte

```powershell
C:\Windows\System32\inetsrv\appcmd.exe recycle apppool "AttendanceSystem"
```

```powershell
Restart-Service AttendanceSystem.ZKTeco.Service
```

El programa de escritorio se reinicia cerrándolo y abriéndolo. Si el servicio ZKTeco no arranca o el SDK quedó mal
registrado, [Reinstall-ZKTecoService.ps1](../Reinstall-ZKTecoService.ps1) lo registra como el instalador (sección 4.1).

---

## 3. Usuarios y accesos

Los usuarios se administran en la pantalla de usuarios, con el permiso `Users.Manage`
([Users.razor:3](../../src/Presentation/AttendanceSystem.Blazor.Server/Components/Pages/Users.razor#L3),
[UpdateUserCommand.cs:7](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/UpdateUser/UpdateUserCommand.cs#L7)).

### 3.1 Roles y permisos

Hay tres roles fijos; cada uno es un conjunto de permisos
([Permissions.cs:5-13](../../src/Core/AttendanceSystem.Application/Authorization/Permissions.cs#L5-L13),
[líneas 89-140](../../src/Core/AttendanceSystem.Application/Authorization/Permissions.cs#L89-L140)).
**Administrador** tiene todos; **RH** tiene los de Usuario y otros más; **Usuario** tiene los básicos. La explicación
para el usuario final irá en el manual de RH.

| Permiso | Para qué | Usuario | RH | Administrador |
|---|---|:-:|:-:|:-:|
| `Employees.View` | Ver empleados | ✓ | ✓ | ✓ |
| `Employees.Create`, `Employees.Edit` | Dar de alta y editar datos generales | ✓ | ✓ | ✓ |
| `Employees.Import`, `Employees.Export` | Importar y exportar empleados | ✓ | ✓ | ✓ |
| `Employees.SendToDevice` | Enviar empleados a los relojes | ✓ | ✓ | ✓ |
| `ExternalEmployees.Create` | Alta de empleados de sucursales externas | ✓ | ✓ | ✓ |
| `Catalogs.View` | Ver catálogos | ✓ | ✓ | ✓ |
| `Devices.Download` | Descargar un reloj | ✓ | ✓ | ✓ |
| `ClockAlerts.View` | Ver alertas de hora de relojes | ✓ | ✓ | ✓ |
| `Attendance.View`, `Reports.View` | Ver asistencia y reportes | ✓ | ✓ | ✓ |
| `Employees.EditSchedule` | Turno, horario, esquema y descansos | | ✓ | ✓ |
| `Employees.EditOvertime` | Autorización y reglas de horas extra | | ✓ | ✓ |
| `Employees.Leave` | Vacaciones, incapacidades y bajas temporales | | ✓ | ✓ |
| `ExternalEmployees.Edit` | Editar empleados externos | | ✓ | ✓ |
| `Positions.Manage`, `Roster.Manage` | Puestos y rol de turnos | | ✓ | ✓ |
| `Attendance.Calculate`, `Attendance.Adjust` | Recalcular y ajustar la asistencia | | ✓ | ✓ |
| `Attendance.ManualPunch`, `Attendance.ViewHistory` | Marcajes manuales e historial | | ✓ | ✓ |
| `Attendance.ClosePeriod` | Ver y cerrar periodos | | ✓ | ✓ |
| `Reports.PayrollExport` | Exportar para nómina | | ✓ | ✓ |
| `Employees.Delete`, `Employees.DevicePrivilege` | Borrar empleados; privilegio en el reloj | | | ✓ |
| `ExternalEmployees.Delete` | Borrar empleados externos | | | ✓ |
| `Branches.Manage`, `Departments.Manage`, `Positions.Delete`, `Shifts.Manage` | Sucursales, departamentos, borrar puestos, turnos | | | ✓ |
| `Devices.Manage`, `Devices.DownloadAll`, `Devices.ForceFullDownload` | Relojes, descarga de todos, re-descarga completa, cuarentena | | | ✓ |
| `ClockAlerts.Resolve` | Resolver alertas de hora | | | ✓ |
| `Attendance.VoidManualPunch`, `Attendance.ReopenPeriod` | Anular marcajes manuales; reabrir periodos | | | ✓ |
| `Settings.Manage`, `Backups.Manage`, `Users.Manage`, `Jobs.View` | Configuración, respaldos, usuarios y panel de tareas | | | ✓ |

Cada comando revisa su permiso (`RequiresPermission`); una petición sin usuario la ejecuta el sistema, como las tareas
programadas y ADMS ([PermissionService.cs:18-41](../../src/Core/AttendanceSystem.Application/Authorization/PermissionService.cs#L18-L41)).
Pruebas: [PermissionsAndAuditTests.cs](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Phase07/PermissionsAndAuditTests.cs).

### 3.2 Contraseñas, bloqueo y sesión

| Regla | Valor | Código |
|---|---|---|
| Contraseña | 6 caracteres o más, con dígito, minúscula y mayúscula; sin símbolo obligatorio | [Program.cs:95-102](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L95-L102), [WpfBackendHost.cs:94-101](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L94-L101) |
| Bloqueo | 5 intentos fallidos bloquean 15 minutos, en el sitio y en el programa | [Program.cs:107-110](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L107-L110), [WpfBackendHost.cs:103-106](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L103-L106), [AuthenticationService.cs:37-38](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/AuthenticationService.cs#L37-L38) |
| Sesión del sitio | 8 horas, renovada con el uso | [Program.cs:116-124](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L116-L124) |
| Usuario desactivado o con contraseña cambiada | Su sesión abierta del sitio se cierra en 2 minutos o menos | [IdentityRevalidatingAuthenticationStateProvider.cs:29](../../src/Presentation/AttendanceSystem.Blazor.Server/Services/IdentityRevalidatingAuthenticationStateProvider.cs#L29), [líneas 41-57](../../src/Presentation/AttendanceSystem.Blazor.Server/Services/IdentityRevalidatingAuthenticationStateProvider.cs#L41-L57) |
| Usuario desactivado | No puede iniciar sesión | [AuthenticationService.cs:31](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/AuthenticationService.cs#L31), [AuthenticationStateService.cs:65](../../src/Presentation/AttendanceSystem.WPF/Services/AuthenticationStateService.cs#L65) |

### 3.3 Recuperar el acceso

- **Un usuario olvidó su contraseña.** Un Administrador la cambia al editarlo (campo de contraseña nueva)
  ([UpdateUserCommand.cs:14](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/UpdateUser/UpdateUserCommand.cs#L14),
  [líneas 60-70](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/UpdateUser/UpdateUserCommand.cs#L60-L70)).
- **Un usuario quedó bloqueado.** Se desbloquea solo a los 15 minutos. Para no esperar, en SQL:

  ```sql
  UPDATE "AspNetUsers" SET "LockoutEnd" = NULL, "AccessFailedCount" = 0 WHERE "UserName" = '<usuario>';
  ```

- **La primera contraseña de `admin`.** Ver [arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento):
  `InitialAdmin:Password`, `INITIAL_ADMIN_PASSWORD` o `.initial_admin_credentials.txt` en la carpeta del programa que
  arrancó primero.
- **No queda ningún Administrador que pueda entrar.** El sistema no trae un procedimiento. El arranque sólo crea `admin`
  si no existe un usuario con ese nombre ([IdentityDataSeeder.cs:38-41](../../src/Infrastructure/AttendanceSystem.Infrastructure/Persistence/IdentityDataSeeder.cs#L38-L41)).
  Por eso, renombrar el usuario `admin` existente y reiniciar el sitio debería crear uno nuevo con contraseña nueva:

  ```sql
  UPDATE "AspNetUsers" SET "UserName" = 'admin_anterior', "NormalizedUserName" = 'ADMIN_ANTERIOR' WHERE "NormalizedUserName" = 'ADMIN';
  ```

  **Deducido del código, no probado.** Haga antes un respaldo.

---

## 4. Diagnóstico

### 4.1 Scripts

| Script | Qué hace | Cambia algo |
|---|---|---|
| [Diagnose-AttendanceSystem.ps1](../Diagnose-AttendanceSystem.ps1) | Revisa el servicio ZKTeco y su `ApiKey`, el puerto gRPC, el registro COM de 32 bits del SDK, el sitio y su grupo de aplicaciones en IIS, la configuración del programa de escritorio y `Hosting:Standalone`, los puertos ADMS y de PostgreSQL, las reglas de firewall y los últimos errores de cada bitácora. No muestra contraseñas ni la `ApiKey` | No |
| [Reinstall-ZKTecoService.ps1](../Reinstall-ZKTecoService.ps1) | Detiene y elimina el servicio, registra `zkemkeeper.dll` con el `regsvr32` de 32 bits **desde la carpeta del servicio**, lo crea, lo inicia y comprueba su puerto | Sí: requiere administrador |

```powershell
powershell -ExecutionPolicy Bypass -File .\Docs\Diagnose-AttendanceSystem.ps1
```

Las dos aceptan otra carpeta (`-InstallDir` y `-ServiceDir`); el diagnóstico acepta otro puerto ADMS (`-AdmsPort`).
Como administrador, el diagnóstico también revisa IIS; sin ese permiso lo indica como alerta. Termina con código 1 si
hubo alguna falla.

El diagnóstico se ejecutó en un equipo con el sistema instalado (sitio y programa) y dio resultados correctos. El de
reinstalación sólo se revisó con el analizador de PowerShell: no se ejecutó, porque elimina el servicio.

### 4.2 Bitácoras

| Parte | Carpeta | Qué buscar primero | Detalle |
|---|---|---|---|
| Sitio web | `Web\logs\` | `errors\errors-*.log`; si el sitio no arranca, `bootstrap\bootstrap-*.txt` | [instalación, 10.1](05-instalacion-configuracion.md#101-sitio-web-y-programa-de-escritorio) |
| Programa de escritorio | `Desktop\logs\wpf-*.log` | Líneas `[ERR]` y `[FTL]` | [appsettings.Desktop.template.json:29-38](../../Deploy/appsettings.Desktop.template.json#L29-L38) |
| Servicio ZKTeco | `Service\logs\` | `errors\zkteco-errors-*.log`; `zkteco\zkteco-service-*.log` para ver cada conexión | [Program.cs:98-114](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L98-L114) |
| Instalador | `Logs\` | Según el paso que falló | [instalación, 6](05-instalacion-configuracion.md#6-verificación-final-y-bitácoras-del-instalador) |

Las peticiones de los relojes ADMS quedan completas (cabeceras y cuerpo, sin datos sensibles) en
`Web\logs\adms\adms-*.log` ([relojes, 3.1](04-dispositivos.md#31-dónde-escucha-y-cómo-se-identifica-el-reloj)).

### 4.3 Panel de tareas (Hangfire)

En el sitio, `/hangfire`, con el permiso `Jobs.View` (sólo Administrador)
([Program.cs:203-206](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L203-L206),
[HangfireDashboardAuthorizationFilter.cs:8-16](../../src/Presentation/AttendanceSystem.Blazor.Server/Services/HangfireDashboardAuthorizationFilter.cs#L8-L16)).
Muestra las tareas programadas, las que fallaron y sus reintentos. La lista de tareas y sus horarios está en
[arquitectura, 8.1](01-arquitectura.md#81-tareas-de-hangfire).

### 4.4 Alertas del sistema

Se guardan en `SystemAlerts` y se muestran en la interfaz. Las de fallas también se envían por correo si las alertas y
el SMTP están configurados ([instalación, 11.3](05-instalacion-configuracion.md#113-correo-y-alertas)).

| Tipo | Valor | Quién la genera |
|---|---|---|
| `CriticalPositionAbsence` | 1 | Revisión de ausencias en puestos críticos ([CheckCriticalAbsencesCommand.cs:149](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/CheckCriticalAbsences/CheckCriticalAbsencesCommand.cs#L149)) |
| `DeviceOffline` | 2 | Monitoreo de relojes ([relojes, 7](04-dispositivos.md#7-estado-en-línea-y-monitoreo)) |
| `DeviceMemoryFull` | 3 | Monitoreo de relojes, al 90 % de la memoria |
| `DeviceClockDrift` | 4 | Hora de un reloj desviada ([relojes, 5.4](04-dispositivos.md#54-verificación-y-resincronización)) |
| `PunchQuarantined` | 5 | Marcajes apartados en cuarentena ([relojes, 6](04-dispositivos.md#6-cuarentena-de-marcajes)) |
| `ScheduleIntegrity` | 6 | Revisión de integridad de horarios ([CheckScheduleIntegrityCommand.cs:77](../../src/Core/AttendanceSystem.Application/Features/Shifts/Commands/CheckScheduleIntegrity/CheckScheduleIntegrityCommand.cs#L77)) |
| `SystemError` | 99 | Tareas automáticas que agotaron sus reintentos, relojes sin descargar, cierres pospuestos. Se avisa una sola vez mientras siga abierta ([OperationalAlerts.cs:8-60](../../src/Core/AttendanceSystem.Application/Features/Automation/OperationalAlerts.cs#L8-L60)) |

Valores en [AlertType.cs](../../src/Core/AttendanceSystem.Domain/Enumerations/AlertType.cs).

### 4.5 Consultas de salud

Las consultas de verificación (versión del esquema, checadas de un empleado, filas huérfanas, bloqueos activos) y cómo
revisar la cola de recálculo están en [base de datos, secciones 9.6 y 10](02-base-de-datos.md#10-consultas-de-verificación).
La del estado de mantenimiento está en la sección 2.1 de este documento.

---

## 5. Índice de síntomas

Cada documento tiene su tabla de problemas; éste es el índice.

| Síntoma | Dónde |
|---|---|
| El sitio no abre o responde 500.30 | [Instalación, 12](05-instalacion-configuracion.md#12-problemas-posibles-y-cómo-resolverlos) |
| El sitio o el programa dicen que están en mantenimiento | Sección 2 de este documento; [base de datos, 9.1](02-base-de-datos.md#91-el-sistema-arranca-en-mantenimiento-después-de-actualizar) |
| La migración se cancela al actualizar | [Base de datos, 9.2 y 9.3](02-base-de-datos.md#92-la-migración-se-cancela-por-lock_timeout) |
| Las checadas no se reflejan en la asistencia | [Base de datos, 9.5 y 9.6](02-base-de-datos.md#95-checadas-que-desaparecen-del-cálculo); [relojes, 6](04-dispositivos.md#6-cuarentena-de-marcajes) |
| Un cálculo de asistencia no da lo esperado | [Motor de cálculo](03-motor-de-calculo.md) |
| Un reloj no conecta, no envía o tiene la hora mal | [Relojes, 8](04-dispositivos.md#8-problemas-posibles-y-cómo-resolverlos) |
| Un respaldo o una restauración fallan | Sección 1 de este documento |
| Alguien no puede entrar | Sección 3.3 de este documento |
| No llegan correos | [Instalación, 12](05-instalacion-configuracion.md#12-problemas-posibles-y-cómo-resolverlos) |
| La instalación terminó con problemas | [Instalación, 6 y 12](05-instalacion-configuracion.md#6-verificación-final-y-bitácoras-del-instalador) |

---

## 6. Comportamientos verificados que conviene revisar

Se confirmaron con la lectura del código o al ejecutar los scripts; se tratarán en la fase de limitaciones.

1. **El respaldo completo del sitio copia el `appsettings.json` equivocado.** Copia el `appsettings.json` de la carpeta
   del programa ([BackupService.cs:191-203](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L191-L203)),
   pero el instalador escribe la configuración del sitio en `appsettings.Production.json`
   ([instalación, 4](05-instalacion-configuracion.md#4-archivos-de-configuración-que-escribe)). En el programa de
   escritorio sí es el archivo correcto. En cualquier caso, ese archivo lleva la contraseña de la base y la `ApiKey`,
   y el `.zip` no está cifrado.
2. **La validación posterior a restaurar es débil:** basta con que existan 2 de las tablas `AspNetUsers`, `Employees` y
   `AttendanceRecords` ([BackupService.cs:1897-1927](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1897-L1927)).
3. **Nada impide borrar o desactivar al último Administrador**: los comandos de usuarios no lo revisan
   ([DeleteUserCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/DeleteUser/DeleteUserCommand.cs),
   [UpdateUserCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/UpdateUser/UpdateUserCommand.cs)).
4. **No hay forma de salir de `Failed` sin restaurar un respaldo** (sección 2.2).
5. **La retención cuenta juntos los respaldos completos y los de sólo base**, así que muchos respaldos de sólo base
   pueden desplazar a los completos (sección 1.3).
6. **Los scripts de `Docs/` estaban rotos y se corrigieron en esta fase.** El diagnóstico no se podía ejecutar:
   PowerShell rechazaba `"$DbHost:$DbPort"` en las líneas 101 y 103. Además buscaba el SDK en `SysWOW64` (el instalador
   lo registra en `Service\`), buscaba errores en el Visor de eventos (el servicio escribe en archivos) y no revisaba ADMS,
   IIS ni la configuración. El de reinstalación copiaba todas las DLL del servicio a `SysWOW64`, lo que puede romper
   otros programas de ZKTeco ([setup_script.iss:135-139](../../Deploy/setup_script.iss#L135-L139)), y no revisaba el
   resultado de `regsvr32`.
