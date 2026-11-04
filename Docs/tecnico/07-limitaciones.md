# 07 · Limitaciones conocidas y defectos

Lista de seguimiento de todo lo que la revisión de los documentos 01 a 06 encontró, más una revisión de seguridad
dedicada. Cada hallazgo tiene una ficha: tipo, gravedad, a quién afecta, cómo comprobarlo, qué puede hacer soporte
mientras tanto y qué corrección se recomienda en el código.

- **Limitación de diseño (L):** el código hace lo que pretende, pero eso restringe el uso o tiene un riesgo conocido.
- **Defecto (D):** el código hace algo distinto de lo que parece pretender, o el resultado es incorrecto.
- **Por confirmar (C):** reglas de cálculo comprobadas con pruebas cuya corrección depende de lo que RH espere.
- Las fichas de seguridad llevan la marca 🔒.

**Gravedad.**

| Gravedad | Criterio |
|---|---|
| Alta | Puede perder o alterar datos, impide usar el sistema, o permite acceso a datos sin autorización |
| Media | Da resultados incorrectos o falla, pero hay una solución temporal |
| Baja | Confunde, estorba o sólo ocurre en casos poco comunes |

**Base de la revisión.** Las citas apuntan al código de esta rama, que parte de `main` en `a019748`. Lo que se fusionó
después en `main` (#31, #34: reportes y configuración del WPF, horas extra del empleado) no toca el código de ninguna
ficha. Lo que ya se corrigió en otra rama lo dice su ficha.

Índice

1. [Resumen](#1-resumen)
2. [Defectos](#2-defectos)
3. [Limitaciones de diseño](#3-limitaciones-de-diseño)
4. [Reglas de cálculo por confirmar](#4-reglas-de-cálculo-por-confirmar)
5. [Corregidos durante la revisión](#5-corregidos-durante-la-revisión)

---

## 1. Resumen

| ID | Hallazgo | Gravedad | Área |
|---|---|---|---|
| [D01](#d01-borrar-tras-descargar-por-sdk-borra-el-rango-equivocado) | Borrar tras descargar por SDK borra el rango equivocado | Alta | Relojes |
| [D02](#d02-el-sitio-web-de-v230-beta1-no-arranca) | El sitio web de v2.3.0-beta.1 no arranca | Alta | Instalación |
| [D03](#d03--la-apikey-del-servicio-zkteco-abre-dos-rutas-del-sitio) 🔒 | La `ApiKey` del servicio ZKTeco abre dos rutas del sitio | Media | Seguridad |
| [D04](#d04-el-stamp-de-adms-mezcla-hora-civil-y-utc) | El `Stamp` de ADMS mezcla hora civil y UTC | Media | Relojes |
| [D05](#d05-el-estado-en-línea-de-adms-depende-del-proceso) | El estado en línea de ADMS depende del proceso | Media | Relojes |
| [D06](#d06--un-appsettingsjson-local-se-incluye-en-el-instalador) 🔒 | Un `appsettings.json` local se incluye en el instalador | Media | Instalación |
| [D07](#d07-cada-actualización-borra-y-recrea-el-sitio-de-iis) | Cada actualización borra y recrea el sitio de IIS | Media | Instalación |
| [D08](#d08-cambiar-el-puerto-adms-no-ajusta-iis-ni-el-firewall) | Cambiar el puerto ADMS no ajusta IIS ni el firewall | Media | Instalación |
| [D09](#d09--el-respaldo-completo-del-sitio-guarda-la-configuración-equivocada) 🔒 | El respaldo completo del sitio guarda la configuración equivocada | Media | Respaldos |
| [D10](#d10-la-validación-posterior-a-restaurar-es-débil) | La validación posterior a restaurar es débil | Media | Respaldos |
| [D11](#d11-nada-impide-quitar-al-último-administrador) | Nada impide quitar al último Administrador | Media | Usuarios |
| [D12](#d12-adms-interpreta-la-hora-unix-con-la-zona-del-servidor) | ADMS interpreta la hora Unix con la zona del servidor | Media | Relojes |
| [D13](#d13-hikvision-dice-que-borró-las-huellas-sin-borrarlas) | Hikvision dice que borró las huellas sin borrarlas | Media | Relojes |
| [D14](#d14-la-lectura-de-usuarios-por-adms-usa-tablas-de-acceso-en-relojes-att) | La lectura de usuarios por ADMS usa tablas de acceso en relojes `att` | Media | Relojes |
| [D15](#d15-migraciones-con-fecha-futura) | Migraciones con fecha futura | Media (hasta el 14/11/2026) | Base de datos |
| [D16](#d16-cinco-campos-de-configuración-y-las-claves-sendgrid-no-hacen-nada) | Cinco campos de configuración y las claves `SendGrid` no hacen nada | Baja | Configuración |
| [D17](#d17-la-retención-mezcla-respaldos-completos-y-de-sólo-base) | La retención mezcla respaldos completos y de sólo base | Baja | Respaldos |
| [D18](#d18-un-respaldo-falla-si-la-conexión-no-tiene-contraseña) | Un respaldo falla si la conexión no tiene contraseña | Baja | Respaldos |
| [D19](#d19-subir-un-respaldo-de-más-de-286-mb-por-la-api-falla-en-iis) | Subir un respaldo de más de 28.6 MB por la API falla en IIS | Baja | Respaldos |
| [D20](#d20--cerrar-sesión-por-get) 🔒 | Cerrar sesión por `GET` | Baja | Seguridad |
| [D21](#d21-la-alerta-de-cuarentena-muestra-el-identificador-del-reloj) | La alerta de cuarentena muestra el identificador del reloj | Baja | Relojes |
| [D22](#d22-puertos-por-omisión-inconsistentes) | Puertos por omisión inconsistentes | Baja | Configuración |
| [D23](#d23-paquetes-sin-uso-y-comentario-desactualizado) | Paquetes sin uso y comentario desactualizado | Baja | Código |
| [L01](#l01--secretos-legibles-en-la-base-y-en-los-archivos-de-configuración) 🔒 | Secretos legibles en la base y en los archivos de configuración | Alta | Seguridad |
| [L02](#l02--el-sitio-sólo-se-sirve-por-http) 🔒 | El sitio sólo se sirve por HTTP | Alta | Seguridad |
| [L03](#l03--adms-confía-en-el-número-de-serie) 🔒 | ADMS confía en el número de serie | Alta | Seguridad |
| [L04](#l04--el-programa-de-escritorio-se-conecta-directo-a-la-base) 🔒 | El programa de escritorio se conecta directo a la base | Alta | Seguridad |
| [L05](#l05--las-checadas-de-la-api-externa-no-pasan-por-la-cuarentena) 🔒 | Las checadas de la API externa no pasan por la cuarentena | Media | Seguridad |
| [L06](#l06-relojes-zkteco-con-comm-key) | Relojes ZKTeco con *comm key* | Media | Relojes |
| [L07](#l07-adms-sólo-admite-zonas-de-horas-enteras) | ADMS sólo admite zonas de horas enteras | Media | Relojes |
| [L08](#l08-hikvision-es-experimental) 🔒 | Hikvision es experimental | Media | Relojes |
| [L09](#l09-sin-sitio-web-todo-depende-del-programa-abierto) | Sin sitio web, todo depende del programa abierto | Media | Arquitectura |
| [L10](#l10-la-descarga-por-sdk-lee-toda-la-memoria-del-reloj) | La descarga por SDK lee toda la memoria del reloj | Media | Relojes |
| [L11](#l11-sólo-una-restauración-exitosa-saca-del-estado-failed) | Sólo una restauración exitosa saca del estado `Failed` | Media | Operación |
| [L12](#l12--desinstalar-deja-datos-y-secretos) 🔒 | Desinstalar deja datos y secretos | Media | Instalación |
| [L13](#l13-adms-sólo-puede-vaciar-el-reloj-completo) | ADMS sólo puede vaciar el reloj completo | Baja | Relojes |
| [L14](#l14-el-tiempo-real-por-sdk-sólo-funciona-con-el-sitio-web) | El tiempo real por SDK sólo funciona con el sitio web | Baja | Relojes |
| [L15](#l15-coordinación-en-memoria-de-cada-proceso) | Coordinación en memoria de cada proceso | Baja | Arquitectura |
| [L16](#l16--canal-grpc-sin-cifrar) 🔒 | Canal gRPC sin cifrar | Baja | Seguridad |
| [L17](#l17--roles-fijos-y-contraseñas-cortas) 🔒 | Roles fijos y contraseñas cortas | Baja | Usuarios |
| [L18](#l18-sin-copia-de-respaldos-fuera-del-equipo) | Sin copia de respaldos fuera del equipo | Baja | Respaldos |
| [L19](#l19-otros-límites-menores) | Otros límites menores | Baja | Varios |
| [C01–C08](#4-reglas-de-cálculo-por-confirmar) | Reglas de cálculo por confirmar con RH | — | Cálculo |

---

## 2. Defectos

### D01 Borrar tras descargar por SDK borra el rango equivocado

- **Gravedad:** Alta. **Afecta a:** relojes ZKTeco por SDK con "borrar tras descargar" activado.
- **Qué pasa:** el rango que se borra del reloj se envía en UTC, no en su hora civil. En UTC−6, si se leyeron marcajes
  de 08:00 a 18:00, el reloj borra de 14:00 a 00:00. Quedan sin borrar las primeras horas y **se borran checadas
  hechas después de la lectura, sin haberse descargado**.
- **Cómo comprobarlo:** prueba `D01`
  ([DispositivosEjemplosTests.cs:70](../../tests/AttendanceSystem.Application.UnitTests/Features/Devices/Documentation/DispositivosEjemplosTests.cs#L70));
  detalle en [relojes, 9.1](04-dispositivos.md#9-comportamientos-verificados-que-conviene-revisar).
- **Solución temporal:** desactivar "borrar tras descargar" en todos los relojes SDK y vaciarlos a mano después de
  comprobar que sus marcajes están en el sistema.
- **Corrección recomendada:** pasar a `ClearLogsAsync` el primer y el último marcaje en la hora civil del reloj
  (`OriginalCheckTime`), no `CheckTime`
  ([DownloadFromDeviceCommand.cs:435-436](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/DownloadFromDevice/DownloadFromDeviceCommand.cs#L435-L436),
  [línea 507](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/DownloadFromDevice/DownloadFromDeviceCommand.cs#L507)),
  e invertir `D01`. Cambiar también la prueba `DownloadFromDeviceSafetyTests`, que simula la conversión de zona como
  identidad y por eso no lo detecta.

### D02 El sitio web de v2.3.0-beta.1 no arranca

- **Gravedad:** Alta. **Afecta a:** instalaciones con sitio web hechas con el instalador de v2.3.0-beta.1 (compilado
  en CI).
- **Qué pasa:** la plantilla de configuración del sitio fija `Serilog:WriteTo:3` sin el `appsettings.json` base que
  la completa; Serilog lanza una excepción al construir el host y el proceso termina.
- **Cómo comprobarlo:** pruebas `I01` e `I02`
  ([InstalacionDocumentacionTests.cs:28](../../tests/AttendanceSystem.Infrastructure.Tests/Documentation/InstalacionDocumentacionTests.cs#L28));
  detalle en [instalación, 13.1](05-instalacion-configuracion.md#13-comportamientos-verificados-que-conviene-revisar).
- **Solución temporal:** en `Web\appsettings.Production.json`, borrar el bloque `"WriteTo"` de `Serilog` y reciclar el
  grupo de aplicaciones.
- **Corrección recomendada:** en curso en
  [Aletsis/AttendanceSystem#33](https://github.com/Aletsis/AttendanceSystem/pull/33) (abierto). Al fusionarlo, invertir
  `I01` e `I02` y actualizar el documento 05.

### D03 🔒 La `ApiKey` del servicio ZKTeco abre dos rutas del sitio

- **Gravedad:** Media. **Afecta a:** el sitio web.
- **Qué pasa:** `POST /api/configuration/reload-jobs` y `POST /api/external-logs/receive` aceptan, en lugar de una
  sesión de Administrador, la cabecera `x-api-key` con `ApiKey` o `ZKTecoService:ApiKey`, la misma clave del servicio
  ZKTeco. Quien la tenga puede registrar checadas de cualquier empleado. La comparación no es de tiempo constante,
  a diferencia de la del servicio.
- **Cómo comprobarlo:** [ExternalLogsController.cs:44-56](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ExternalLogsController.cs#L44-L56),
  [ConfigurationController.cs:35-45](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ConfigurationController.cs#L35-L45);
  comparar con [ApiKeyAuthInterceptor.cs:92-103](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Interceptors/ApiKeyAuthInterceptor.cs#L92-L103).
- **Solución temporal:** si no se usan esas rutas, no hay nada que hacer en la configuración: la clave es obligatoria
  para el servicio. Tratar la `ApiKey` como contraseña de administrador.
- **Corrección recomendada:** una clave propia para cada ruta (por ejemplo `ExternalLogs:ApiKey`, que hoy sólo se lee
  si faltan las otras dos), comparada con `CryptographicOperations.FixedTimeEquals`.

### D04 El `Stamp` de ADMS mezcla hora civil y UTC

- **Gravedad:** Media (impacto no comprobado). **Afecta a:** relojes ADMS.
- **Qué pasa:** `Devices.LastDownloadAt`, que se envía al reloj como `Stamp`, queda con la hora civil del último
  marcaje tras un lote limpio, pero en UTC tras un lote con una línea mala o tras el acuse de una descarga. En UTC−6
  queda 6 horas adelantado. Si el firmware usa el `Stamp` para decidir qué ya entregó, podría no reenviar marcajes.
- **Cómo comprobarlo:** pruebas `S01` a `S04`
  ([AdmsStampDocumentacionTests.cs](../../tests/AttendanceSystem.Blazor.UnitTests/Controllers/AdmsStampDocumentacionTests.cs));
  detalle en [relojes, 3.6](04-dispositivos.md#36-qué-pasa-con-un-lote-de-marcajes).
- **Solución temporal:** si faltan marcajes de un reloj ADMS, descargarlo con fechas (consulta `DATA QUERY` con rango),
  que no depende del `Stamp`.
- **Corrección recomendada:** guardar el cursor ADMS en su propia columna, siempre en la hora civil del reloj, y no
  reutilizar `LastDownloadAt`. Probar con un reloj real qué hace el firmware con el `Stamp`.

### D05 El estado en línea de ADMS depende del proceso

- **Gravedad:** Media (deducido del código, no reproducido). **Afecta a:** instalaciones con sitio web y programa de
  escritorio abiertos a la vez, con alertas activadas.
- **Qué pasa:** la actividad de los relojes ADMS se guarda en la memoria del proceso que los recibe, pero el monitoreo
  es una tarea de Hangfire que puede correr en el programa de escritorio. Ahí no ve actividad y marcaría los relojes
  fuera de línea, con alerta y correo.
- **Cómo comprobarlo:** [relojes, 9.3](04-dispositivos.md#9-comportamientos-verificados-que-conviene-revisar).
- **Solución temporal:** si aparecen alertas `DeviceOffline` de relojes ADMS que sí están enviando, ignorarlas; se
  resuelven en la siguiente pasada que corra en el sitio.
- **Corrección recomendada:** guardar la última actividad en la base, o ejecutar esa revisión sólo en el proceso que
  recibe ADMS.

### D06 🔒 Un `appsettings.json` local se incluye en el instalador

- **Gravedad:** Media. **Afecta a:** instaladores compilados en el equipo de un desarrollador.
- **Qué pasa:** `dotnet publish` copia el `appsettings.json` del proyecto si existe, y el instalador sólo excluye
  `appsettings.Production.json` del sitio y del servicio. La configuración local del desarrollador, con lo que contenga,
  llega a los equipos donde se instale. En el equipo donde se escribió este documento, `Service\appsettings.json`
  existe junto al de producción.
- **Cómo comprobarlo:** [instalación, 13.2](05-instalacion-configuracion.md#13-comportamientos-verificados-que-conviene-revisar);
  `Get-ChildItem "C:\Program Files\AttendanceSystem\Service","C:\Program Files\AttendanceSystem\Web" -Filter appsettings.json`.
- **Solución temporal:** compilar el instalador sólo en CI.
- **Corrección recomendada:** excluir `appsettings.json` en `Prepare-Release.ps1` y en `[Files]`. Según su descripción,
  [Aletsis/AttendanceSystem#33](https://github.com/Aletsis/AttendanceSystem/pull/33) lo hace.

### D07 Cada actualización borra y recrea el sitio de IIS

- **Gravedad:** Media. **Afecta a:** instalaciones con sitio web que tengan cambios hechos a mano en IIS.
- **Qué pasa:** `configure_iis.bat` borra el sitio `AttendanceSystem` y lo crea de nuevo en cada instalación, y detiene
  `Default Web Site`. Se pierden los enlaces agregados (HTTPS, otro puerto ADMS) y cualquier ajuste del sitio.
- **Cómo comprobarlo:** [configure_iis.bat:48-56](../../Deploy/configure_iis.bat#L48-L56).
- **Solución temporal:** anotar los cambios hechos en IIS y volver a aplicarlos después de cada actualización.
- **Corrección recomendada:** crear el sitio sólo si no existe y, si existe, actualizar únicamente la ruta física.

### D08 Cambiar el puerto ADMS no ajusta IIS ni el firewall

- **Gravedad:** Media. **Afecta a:** quien cambie `AdmsPort` en la configuración.
- **Qué pasa:** el instalador fija 18373 en el enlace de IIS y en la regla de firewall; cambiar el puerto en la
  pantalla de configuración sólo afecta al programa de escritorio en modo `Standalone`, y sólo al reiniciarlo.
- **Cómo comprobarlo:** [setup_script.iss:33-35](../../Deploy/setup_script.iss#L33-L35),
  [instalación, 11.4](05-instalacion-configuracion.md#114-adms-y-nube).
- **Solución temporal:** después de cambiarlo, agregar el enlace en IIS y ajustar la regla `AttendanceSystem ADMS`.
- **Corrección recomendada:** advertirlo en la pantalla, o quitar el campo y fijar el puerto en la instalación.

### D09 🔒 El respaldo completo del sitio guarda la configuración equivocada

- **Gravedad:** Media. **Afecta a:** respaldos completos hechos desde el sitio.
- **Qué pasa:** el `.zip` incluye el `appsettings.json` de la carpeta del programa, pero el sitio tiene su configuración
  real en `appsettings.Production.json`. Lo que sí incluye lleva contraseñas, y el `.zip` no está cifrado. Al
  restaurar, esa configuración no se aplica.
- **Cómo comprobarlo:** [BackupService.cs:191-203](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L191-L203),
  [operación, 6.1](06-operacion.md#6-comportamientos-verificados-que-conviene-revisar).
- **Solución temporal:** respaldar a mano `Web\appsettings.Production.json` y guardar los respaldos en un lugar con
  acceso restringido.
- **Corrección recomendada:** copiar los `appsettings*.json` que use el proceso, o ninguno; si se incluyen, cifrar el
  paquete o quitar los secretos.

### D10 La validación posterior a restaurar es débil

- **Gravedad:** Media. **Afecta a:** restauraciones.
- **Qué pasa:** después de restaurar sólo se comprueba que existan 2 de las tablas `AspNetUsers`, `Employees` y
  `AttendanceRecords`. Un respaldo de otra base con esas tablas pasaría.
- **Cómo comprobarlo:** [BackupService.cs:1897-1927](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1897-L1927).
- **Solución temporal:** antes de restaurar un respaldo de origen dudoso, revisarlo con `pg_restore -l`.
- **Corrección recomendada:** exigir las tres y validar `__EFMigrationsHistory`, que ya se revisa en el paso siguiente.

### D11 Nada impide quitar al último Administrador

- **Gravedad:** Media. **Afecta a:** administración de usuarios.
- **Qué pasa:** se puede borrar, desactivar o quitar el rol al único Administrador. Sin otro, no hay quien administre
  usuarios, respaldos ni configuración desde la aplicación.
- **Cómo comprobarlo:** [DeleteUserCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/DeleteUser/DeleteUserCommand.cs),
  [UpdateUserCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Users/Commands/UpdateUser/UpdateUserCommand.cs).
- **Solución temporal:** mantener al menos dos Administradores. Recuperación en
  [operación, 3.3](06-operacion.md#33-recuperar-el-acceso).
- **Corrección recomendada:** rechazar la operación si deja el sistema sin Administradores activos.

### D12 ADMS interpreta la hora Unix con la zona del servidor

- **Gravedad:** Media. **Afecta a:** relojes ADMS que envíen la hora como segundos Unix, con el servidor en otra zona.
- **Qué pasa:** la hora Unix se convierte a la hora local del servidor y después se interpreta como hora del reloj.
- **Cómo comprobarlo:** [AdmsController.cs:1150](../../src/Presentation/AttendanceSystem.Adms/AdmsController.cs#L1150).
- **Solución temporal:** poner el servidor en la zona de los relojes ADMS.
- **Corrección recomendada:** un instante Unix ya es UTC: guardarlo como UTC y calcular la hora civil con la zona del
  reloj.

### D13 Hikvision dice que borró las huellas sin borrarlas

- **Gravedad:** Media. **Afecta a:** relojes Hikvision (experimental).
- **Qué pasa:** borrar las huellas de un empleado devuelve éxito sin enviar nada al equipo.
- **Cómo comprobarlo:** [HikvisionDeviceClient.cs:472-478](../../src/Infrastructure/AttendanceSystem.Infrastructure/Adapters/HikvisionDeviceClient.cs#L472-L478).
- **Solución temporal:** borrar las huellas desde el propio equipo.
- **Corrección recomendada:** devolver `false` hasta implementarlo.

### D14 La lectura de usuarios por ADMS usa tablas de acceso en relojes `att`

- **Gravedad:** Media (no comprobado con un reloj). **Afecta a:** relojes ADMS de asistencia.
- **Qué pasa:** leer usuarios encola `DATA QUERY tablename=user` (y `userpic`, `biophoto`, `biodata`) también en relojes
  `att`, que según el propio código rechazan esas tablas con `-1004`.
- **Cómo comprobarlo:** [AdmsDeviceClient.cs:37-39](../../src/Infrastructure/AttendanceSystem.Infrastructure/Adapters/AdmsDeviceClient.cs#L37-L39),
  [líneas 232-235](../../src/Infrastructure/AttendanceSystem.Infrastructure/Adapters/AdmsDeviceClient.cs#L232-L235).
- **Solución temporal:** si el reloj también responde por SDK, la lectura va primero por SDK.
- **Corrección recomendada:** usar `USERINFO` y las tablas de plantillas en relojes `att`, como ya hacen los envíos.

### D15 Migraciones con fecha futura

- **Gravedad:** Media hasta el 14/11/2026; después desaparece sola. **Afecta a:** desarrollo.
- **Qué pasa:** 9 migraciones tienen fecha del 5 al 13 de noviembre de 2026; una nueva creada antes del 14 quedaría
  ordenada antes que ellas.
- **Cómo comprobarlo:** [base de datos, 9.4](02-base-de-datos.md#94-una-migración-nueva-queda-ordenada-antes-que-las-existentes).
- **Solución temporal:** renombrar la migración nueva con un identificador mayor que `20261113120000`.
- **Corrección recomendada:** ninguna en el código; seguir la regla del documento.

### D16 Cinco campos de configuración y las claves `SendGrid` no hacen nada

- **Gravedad:** Baja. **Afecta a:** quien configure el sistema.
- **Qué pasa:** `LateTolerance`, `StandardWorkHours`, `AutoClearDevicesAfterDownload`, `AutoDownloadOnlyToday` y
  `BackupTimeoutMinutes` se guardan pero nada los lee; `SendGrid:*` lo lee un servicio que no está registrado.
- **Cómo comprobarlo:** [instalación, 11.6](05-instalacion-configuracion.md#116-campos-sin-efecto) (con el `grep`
  para comprobarlo) y [10.1](05-instalacion-configuracion.md#101-sitio-web-y-programa-de-escritorio).
- **Solución temporal:** ignorarlos. La tolerancia de retardo se configura en cada turno; el borrado, en cada reloj.
- **Corrección recomendada:** quitarlos de las pantallas y del modelo, o implementarlos. Borrar `SendGridEmailService`.

### D17 La retención mezcla respaldos completos y de sólo base

- **Gravedad:** Baja. **Afecta a:** quien haga respaldos de sólo base con frecuencia.
- **Qué pasa:** se conservan los 10 más recientes de ambos tipos juntos; varios de sólo base pueden borrar los
  completos.
- **Cómo comprobarlo:** [BackupService.cs:752-756](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L752-L756),
  [líneas 1121-1130](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1121-L1130).
- **Solución temporal:** subir `Backup:KeepLastNBackups` o copiar los completos a otra carpeta.
- **Corrección recomendada:** una retención por tipo.

### D18 Un respaldo falla si la conexión no tiene contraseña

- **Gravedad:** Baja. **Afecta a:** instalaciones manuales con autenticación sin contraseña (`trust`, `pgpass`).
- **Qué pasa:** para ocultar la contraseña en la bitácora se ejecuta `arguments.Replace(_postgresPassword, "***")`; con
  la contraseña vacía, `string.Replace` lanza `ArgumentException` y el respaldo termina con error. El instalador
  siempre pone contraseña.
- **Cómo comprobarlo:** [BackupService.cs:67](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L67),
  [línea 1300](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1300).
- **Solución temporal:** usar una cadena de conexión con contraseña.
- **Corrección recomendada:** reemplazar sólo si la contraseña no está vacía (la contraseña ni siquiera va en los
  argumentos: viaja en `PGPASSWORD`).

### D19 Subir un respaldo de más de 28.6 MB por la API falla en IIS

- **Gravedad:** Baja (deducido, no probado). **Afecta a:** `POST /api/backup/upload` bajo IIS.
- **Qué pasa:** la ruta acepta hasta 1 GB, pero el filtro de peticiones de IIS limita el cuerpo a 30 000 000 bytes por
  omisión y `web.config` no lo amplía. La pantalla de respaldos sube por la conexión de Blazor y no le aplica.
- **Cómo comprobarlo:** [BackupController.cs:58-59](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/BackupController.cs#L58-L59),
  [web.config](../../src/Presentation/AttendanceSystem.Blazor.Server/web.config).
- **Solución temporal:** subir desde la pantalla, o copiar el archivo directo a la carpeta de respaldos.
- **Corrección recomendada:** agregar `requestLimits maxAllowedContentLength` en `web.config`.

### D20 🔒 Cerrar sesión por `GET`

- **Gravedad:** Baja. **Afecta a:** el sitio.
- **Qué pasa:** `GET /Account/Logout` cierra la sesión; cualquier página puede cerrar la sesión de un usuario con un
  enlace o una imagen.
- **Cómo comprobarlo:** [AccountController.cs:92-95](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/AccountController.cs#L92-L95).
- **Solución temporal:** ninguna necesaria; sólo causa molestia.
- **Corrección recomendada:** `POST` con antiforgery, como el inicio de sesión.

### D21 La alerta de cuarentena muestra el identificador del reloj

- **Gravedad:** Baja. **Afecta a:** alertas de lotes ADMS y de tiempo real.
- **Cómo comprobarlo:** [RecordAttendanceBatchCommand.cs:130](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/RecordAttendance/RecordAttendanceBatchCommand.cs#L130).
- **Solución temporal:** buscar el identificador en la lista de relojes.
- **Corrección recomendada:** pasar el nombre del reloj, como en la descarga.

### D22 Puertos por omisión inconsistentes

- **Gravedad:** Baja. **Afecta a:** quien lea la bitácora o la pantalla de configuración.
- **Qué pasa:** el puerto ADMS es 18373 en el dominio y 16373 en la pantalla, el DTO y el comando
  ([relojes, 9.9](04-dispositivos.md#9-comportamientos-verificados-que-conviene-revisar)); el sitio anuncia en la
  bitácora una interfaz en el puerto 16372 que no usa ([arquitectura, 11](01-arquitectura.md#11-observaciones-verificadas)).
- **Solución temporal:** ignorar esos valores; mandan el enlace de IIS y `SystemConfiguration.AdmsPort`.
- **Corrección recomendada:** una sola constante.

### D23 Paquetes sin uso y comentario desactualizado

- **Gravedad:** Baja. **Afecta a:** desarrollo.
- **Cómo comprobarlo:** [arquitectura, 11](01-arquitectura.md#11-observaciones-verificadas).
- **Corrección recomendada:** quitar las referencias y corregir el comentario.

---

## 3. Limitaciones de diseño

### L01 🔒 Secretos legibles en la base y en los archivos de configuración

- **Gravedad:** Alta. **Afecta a:** todas las instalaciones.
- **Qué pasa:** la contraseña SMTP, la cadena de conexión a la nube, las credenciales de los relojes y la contraseña de
  cada empleado en el reloj se guardan sin cifrar en la base y en sus respaldos
  ([base de datos, 3.6](02-base-de-datos.md#36-datos-sensibles-sin-cifrar)). Los `appsettings` del sitio, del
  programa y del servicio, con la contraseña de la base y la `ApiKey`, quedan con los permisos normales de
  `Program Files`: cualquier usuario del equipo puede leerlos. El instalador no les cambia los permisos
  ([setup_script.iss:740-763](../../Deploy/setup_script.iss#L740-L763)).
- **Solución temporal:** limitar quién inicia sesión en el servidor; quitar el permiso de lectura a `Usuarios` sobre
  los `appsettings` (dejando a SYSTEM, Administradores, `IIS_IUSRS` en el sitio y a los usuarios del programa de
  escritorio, que lo necesitan); guardar los respaldos con acceso restringido.
- **Corrección recomendada:** cifrar esas columnas con Data Protection; en el instalador, restringir los permisos de
  los archivos de configuración.

### L02 🔒 El sitio sólo se sirve por HTTP

- **Gravedad:** Alta. **Afecta a:** instalaciones con sitio web a las que se entra desde otros equipos.
- **Qué pasa:** el instalador crea enlaces HTTP en el 80 (o 8081) y el sitio sólo redirige a HTTPS si se define
  `HTTPS_PORT`; HSTS está desactivado. El usuario y la contraseña del inicio de sesión, y la cookie de sesión, viajan
  sin cifrar por la red.
- **Cómo comprobarlo:** [configure_iis.bat:60-72](../../Deploy/configure_iis.bat#L60-L72),
  [Program.cs:176-188](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L176-L188).
- **Solución temporal:** agregar en IIS un enlace HTTPS con certificado y definir `HTTPS_PORT`. Hay que volver a
  aplicarlo tras cada actualización ([D07](#d07-cada-actualización-borra-y-recrea-el-sitio-de-iis)).
- **Corrección recomendada:** que el instalador ofrezca HTTPS, y activar HSTS y cookies `Secure` cuando lo haya.

### L03 🔒 ADMS confía en el número de serie

- **Gravedad:** Alta. **Afecta a:** instalaciones con el puerto ADMS abierto.
- **Qué pasa:** un reloj ADMS se identifica sólo con su `SN`, por HTTP. Quien conozca un número de serie registrado y
  alcance el puerto puede registrar checadas y, además, cambiar la tarjeta, la contraseña del reloj, la foto, las
  huellas y el privilegio de los empleados existentes.
- **Cómo comprobarlo:** [AdmsController.cs:91-109](../../src/Presentation/AttendanceSystem.Adms/AdmsController.cs#L91-L109),
  [líneas 1060-1085](../../src/Presentation/AttendanceSystem.Adms/AdmsController.cs#L1060-L1085);
  [relojes, 3.1](04-dispositivos.md#31-dónde-escucha-y-cómo-se-identifica-el-reloj).
- **Solución temporal:** restringir la regla de firewall `AttendanceSystem ADMS` a las IP de los relojes
  (`Set-NetFirewallRule -DisplayName "AttendanceSystem ADMS" -RemoteAddress <IP1>,<IP2>`). Revisar `Web\logs\adms\`
  si hay sospechas.
- **Corrección recomendada:** aceptar ADMS sólo desde la IP registrada del reloj, y no aceptar cambios de privilegio
  desde ADMS.

### L04 🔒 El programa de escritorio se conecta directo a la base

- **Gravedad:** Alta. **Afecta a:** equipos con el programa de escritorio.
- **Qué pasa:** el programa tiene en su `appsettings.json` la cadena de conexión del usuario dueño de la base
  ([setup_script.iss:606-609](../../Deploy/setup_script.iss#L606-L609), [init_db.sql:15-19](../../Deploy/init_db.sql#L15-L19)),
  y los permisos se revisan dentro del propio programa
  ([PermissionService.cs:30-41](../../src/Core/AttendanceSystem.Application/Authorization/PermissionService.cs#L30-L41)).
  Quien pueda leer ese archivo (L01) puede leer y cambiar cualquier dato sin pasar por los permisos.
- **Solución temporal:** instalar el programa sólo en equipos de personas de confianza; preferir el sitio web para los
  demás.
- **Corrección recomendada:** que el programa hable con un servicio (el sitio) en lugar de con la base, o usar un rol
  de base con menos privilegios para el cliente.

### L05 🔒 Las checadas de la API externa no pasan por la cuarentena

- **Gravedad:** Media. **Afecta a:** `POST /api/external-logs/receive`.
- **Qué pasa:** la ruta registra la checada con `RecordAttendanceCommand`, que no evalúa la cuarentena. La hora se
  interpreta con la zona por omisión, no con la de la sucursal.
- **Cómo comprobarlo:** [ExternalLogsController.cs:65](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ExternalLogsController.cs#L65),
  [líneas 99-106](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ExternalLogsController.cs#L99-L106).
- **Solución temporal:** enviar la hora con zona (`…Z` o con desfase) y revisar esas checadas (origen `ExternalApi`).
- **Corrección recomendada:** pasarlas por `RecordAttendanceBatchCommand` como las demás.

### L06 Relojes ZKTeco con *comm key*

- **Gravedad:** Media. **Afecta a:** relojes ZKTeco por SDK con contraseña de comunicación.
- **Qué pasa:** la conexión por SDK no envía la clave; esos relojes no conectan.
- **Cómo comprobarlo:** [relojes, 2.3](04-dispositivos.md#23-contrato-grpc).
- **Solución temporal:** poner la *comm key* del reloj en 0, o usar ADMS.
- **Corrección recomendada:** agregar la clave al mensaje gRPC y llamar a `SetCommPassword` antes de conectar.

### L07 ADMS sólo admite zonas de horas enteras

- **Gravedad:** Media. **Afecta a:** relojes ADMS en zonas con desfase de media hora.
- **Cómo comprobarlo:** prueba `D06` ([DispositivosEjemplosTests.cs:142](../../tests/AttendanceSystem.Application.UnitTests/Features/Devices/Documentation/DispositivosEjemplosTests.cs#L142)),
  [relojes, 5.3](04-dispositivos.md#53-zonas-en-adms-sólo-horas-enteras).
- **Solución temporal:** usar SDK en esas zonas. En México no aplica.
- **Corrección recomendada:** ninguna en el sistema: es el formato del protocolo (`TimeZone` en horas).

### L08 Hikvision es experimental

- **Gravedad:** Media. **Afecta a:** relojes Hikvision.
- **Qué pasa:** sólo HTTP con autenticación Basic, sin Digest ni HTTPS; sin borrado de marcajes; sin pruebas contra un
  equipo real ([relojes, 4](04-dispositivos.md#4-hikvision-experimental)).
- **Solución temporal:** no usarlo en producción; si se usa, en una red aislada.
- **Corrección recomendada:** Digest y HTTPS, y pruebas con un equipo real, antes de declararlo soportado.

### L09 Sin sitio web, todo depende del programa abierto

- **Gravedad:** Media. **Afecta a:** instalaciones "sólo programa de escritorio".
- **Qué pasa:** las tareas automáticas y la recepción de relojes ADMS sólo funcionan mientras el programa está abierto
  en algún equipo ([arquitectura, 4](01-arquitectura.md#4-formas-de-instalación)).
- **Solución temporal:** marcar "Abrir el programa al iniciar sesión" en un equipo que esté siempre encendido.
- **Corrección recomendada:** instalar el sitio web cuando se necesiten tareas automáticas o ADMS.

### L10 La descarga por SDK lee toda la memoria del reloj

- **Gravedad:** Media. **Afecta a:** relojes SDK con muchos marcajes guardados.
- **Qué pasa:** cada descarga lee todos los marcajes y filtra después; mientras lee, el reloj no acepta checadas
  ([relojes, 2.5](04-dispositivos.md#25-descarga-de-marcajes)).
- **Solución temporal:** vaciar el reloj periódicamente, a mano y después de comprobar los marcajes (no con "borrar
  tras descargar", por [D01](#d01-borrar-tras-descargar-por-sdk-borra-el-rango-equivocado)).
- **Corrección recomendada:** ninguna sencilla: el SDK no ofrece lectura por rango.

### L11 Sólo una restauración exitosa saca del estado `Failed`

- **Gravedad:** Media. **Afecta a:** soporte, tras una restauración fallida.
- **Qué pasa:** no hay botón ni comando para salir de `Failed`; hay que restaurar un respaldo
  ([operación, 2.2](06-operacion.md#22-cómo-se-sale-de-cada-caso)).
- **Solución temporal:** restaurar el snapshot que indica el mensaje de error.
- **Corrección recomendada:** una acción de Administrador, con confirmación, para volver a `Normal` tras revisar la
  base.

### L12 🔒 Desinstalar deja datos y secretos

- **Gravedad:** Media. **Afecta a:** equipos donde se desinstala el sistema.
- **Qué pasa:** quedan la base, PostgreSQL, los `appsettings` con contraseñas, `postgres_superuser.txt`, las bitácoras
  y los respaldos ([instalación, 7](05-instalacion-configuracion.md#7-actualizar-reinstalar-agregar-partes-y-desinstalar)).
- **Solución temporal:** si el equipo cambia de manos, borrar a mano la carpeta de instalación y la base.
- **Corrección recomendada:** preguntar al desinstalar si se borran los datos.

### L13 ADMS sólo puede vaciar el reloj completo

- **Gravedad:** Baja. **Afecta a:** relojes ADMS.
- **Qué pasa:** `CLEAR ATTLOG` no admite rango; por eso "borrar tras descargar" no se aplica a ADMS (prueba `D05`)
  ([relojes, 3.8](04-dispositivos.md#38-comandos-que-envía-el-sistema)).
- **Solución temporal:** vaciarlo a mano después de comprobar los marcajes.

### L14 El tiempo real por SDK sólo funciona con el sitio web

- **Gravedad:** Baja. **Afecta a:** relojes SDK.
- **Qué pasa:** viene apagado, sólo lo usa el sitio, y el servicio admite un único suscriptor
  ([relojes, 2.7](04-dispositivos.md#27-tiempo-real-por-sdk)).
- **Solución temporal:** usar las descargas programadas, o ADMS.

### L15 Coordinación en memoria de cada proceso

- **Gravedad:** Baja. **Afecta a:** instalaciones con sitio y programa a la vez.
- **Qué pasa:** el candado de descarga por reloj, los contadores de una descarga ADMS y la actividad de los relojes
  viven en la memoria del proceso ([relojes, 2.4 y 3.7](04-dispositivos.md#24-sesiones-y-exclusión-por-reloj)).
  Entre procesos sólo coordinan la base y el candado por IP del servicio ZKTeco.

### L16 🔒 Canal gRPC sin cifrar

- **Gravedad:** Baja. **Afecta a:** el servicio ZKTeco.
- **Qué pasa:** HTTP/2 sin TLS. Por omisión sólo escucha en `localhost` ([arquitectura, 11](01-arquitectura.md#11-observaciones-verificadas)).
- **Solución temporal:** no poner `GrpcBindLocalOnly` en `false`.

### L17 🔒 Roles fijos y contraseñas cortas

- **Gravedad:** Baja. **Afecta a:** administración de usuarios.
- **Qué pasa:** los tres roles no se pueden modificar; la contraseña mínima es de 6 caracteres sin símbolo
  ([operación, 3.1 y 3.2](06-operacion.md#31-roles-y-permisos)).
- **Corrección recomendada:** subir la longitud mínima; los roles configurables ya están previstos en el código
  ([Permissions.cs:89-92](../../src/Core/AttendanceSystem.Application/Authorization/Permissions.cs#L89-L92)).

### L18 Sin copia de respaldos fuera del equipo

- **Gravedad:** Baja. **Afecta a:** todas las instalaciones.
- **Qué pasa:** los respaldos quedan en el mismo equipo que la base ([operación, 1.3](06-operacion.md#13-respaldo-automático-y-retención)).
- **Solución temporal:** apuntar `BackupDirectory` a una unidad de red, o copiar la carpeta con una tarea de Windows.

### L19 Otros límites menores

- El cliente gRPC acepta respuestas de hasta 10 MB ([relojes, 2.2](04-dispositivos.md#22-configuración)).
- El descubrimiento de relojes sólo encuentra los del mismo segmento de red ([relojes, 2.8](04-dispositivos.md#28-descubrimiento-de-relojes)).
- La zona de la sucursal no interviene en la de sus relojes ([relojes, 5.1](04-dispositivos.md#51-de-qué-zona-es-un-reloj)).
- La configuración de un `.zip` no se aplica al restaurar ([operación, 1.4](06-operacion.md#14-restauración)).

---

## 4. Reglas de cálculo por confirmar

Comprobadas con las pruebas del [motor de cálculo, sección 11](03-motor-de-calculo.md#11-comportamientos-verificados-que-conviene-conocer).
Si lo que hace el código es lo que RH espera, quedan como reglas documentadas; si no, son defectos.

| ID | Regla actual | Pregunta para RH |
|---|---|---|
| C01 | Con "extras antes de la entrada", quien llega 08:10 dentro de la tolerancia trabaja 470 min, no 480 | ¿Debe contar desde la hora de entrada cuando se llega tarde dentro de la tolerancia? |
| C02 | Un descanso trabajado se mide contra el horario normal: 120 min de salida anticipada y 0 de extra | ¿El descanso trabajado necesita su propia regla de pago? |
| C03 | Un excedente de comida resta del tiempo trabajado y además genera salida anticipada | ¿Los reportes deben mostrar uno solo? |
| C04 | Una guardia de 24 h seguida de un día laborable pierde la salida marcada después de las 24 h | ¿Las guardias siempre van seguidas de descanso? |
| C05 | El faltante de la bolsa semanal se registra el último día de la semana con bolsa, aunque sea descanso | ¿En qué día debe aparecer? |
| C06 | En multi-marcaje, un intervalo abierto suma 0 | ¿Debe avisar en lugar de contar salida anticipada? |
| C07 | El retardo es el retraso completo, no el exceso sobre la tolerancia | ¿Es la regla del reglamento interno? |
| C08 | En multi-marcaje, el redondeo de entrada usa la hora UTC | Sin efecto en México; ¿se corrige por consistencia? |

---

## 5. Corregidos durante la revisión

| Hallazgo | Dónde se corrigió |
|---|---|
| `Diagnose-AttendanceSystem.ps1` no se podía ejecutar y buscaba el SDK y los errores donde no están | Fase 5 ([operación, 6.6](06-operacion.md#6-comportamientos-verificados-que-conviene-revisar)) |
| `Reinstall-ZKTecoService.ps1` copiaba las DLL del SDK a `SysWOW64` | Fase 5 |
| Documentos anteriores con datos falsos: versión 2.1.6, contraseña `Admin123!`, tabla de bitácoras en PostgreSQL | Fase 1 (aviso en [Docs/README.md](../README.md)); se reemplazan en la fase de limpieza |
