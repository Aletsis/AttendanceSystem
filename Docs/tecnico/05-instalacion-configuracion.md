# 05 · Instalación y configuración

Este documento describe qué hace el instalador paso a paso, cómo instalar sin él, cómo preparar un equipo de
desarrollo, y cada clave de configuración que lee el código, tanto en los archivos `appsettings` como en la
configuración guardada en la base. Cada afirmación cita el archivo y la línea que la respalda.

> **Qué no está comprobado.** El instalador no se ejecutó para escribir este documento: lo que dice sale de
> [setup_script.iss](../../Deploy/setup_script.iss) y de los scripts que llama. Cómo se compila el instalador se
> documentará en la fase de desarrollo y CI.

Documentos relacionados: [arquitectura](01-arquitectura.md) (procesos, formas de instalación y puertos),
[base de datos](02-base-de-datos.md) y [relojes](04-dispositivos.md).

Índice

1. [Requisitos](#1-requisitos)
2. [Qué instala el instalador](#2-qué-instala-el-instalador)
3. [Base de datos durante la instalación](#3-base-de-datos-durante-la-instalación)
4. [Archivos de configuración que escribe](#4-archivos-de-configuración-que-escribe)
5. [IIS, servicio y firewall](#5-iis-servicio-y-firewall)
6. [Verificación final y bitácoras del instalador](#6-verificación-final-y-bitácoras-del-instalador)
7. [Actualizar, reinstalar, agregar partes y desinstalar](#7-actualizar-reinstalar-agregar-partes-y-desinstalar)
8. [Instalación manual](#8-instalación-manual)
9. [Equipo de desarrollo](#9-equipo-de-desarrollo)
10. [Referencia de `appsettings`](#10-referencia-de-appsettings)
11. [Configuración guardada en la base](#11-configuración-guardada-en-la-base)
12. [Problemas posibles y cómo resolverlos](#12-problemas-posibles-y-cómo-resolverlos)
13. [Comportamientos verificados que conviene revisar](#13-comportamientos-verificados-que-conviene-revisar)

---

## 1. Requisitos

| Requisito | Valor | Código |
|---|---|---|
| Sistema operativo | Windows 10 1809, Windows 11, Windows Server 2019 o posterior, de 64 bits | [setup_script.iss:79-83](../../Deploy/setup_script.iss#L79-L83) |
| Permisos | Administrador | [setup_script.iss:77](../../Deploy/setup_script.iss#L77) |
| PostgreSQL | 14 o posterior si ya hay uno; si no hay ninguno, el instalador instala el que trae | [setup_script.iss:234-235](../../Deploy/setup_script.iss#L234-L235), [líneas 1025-1030](../../Deploy/setup_script.iss#L1025-L1030), [líneas 158-161](../../Deploy/setup_script.iss#L158-L161) |
| PostgreSQL incluido | El que descarga `Download-Prerequisites.ps1` (el script lo describe como 16.x) | [Download-Prerequisites.ps1:95-105](../../Deploy/Download-Prerequisites.ps1#L95-L105) |
| PowerShell | Necesario: genera las contraseñas y la `ApiKey` | [setup_script.iss:558-580](../../Deploy/setup_script.iss#L558-L580), [líneas 1215-1232](../../Deploy/setup_script.iss#L1215-L1232) |
| Sitio web | IIS (lo habilita el instalador) y el ASP.NET Core Hosting Bundle de la misma versión de .NET que la aplicación (lo incluye el instalador) | [setup_script.iss:186-195](../../Deploy/setup_script.iss#L186-L195), [líneas 59-63](../../Deploy/setup_script.iss#L59-L63) |
| Programa de escritorio y servicio | No necesitan .NET instalado: se publican autocontenidos | [Prepare-Release.ps1:17-29](../../Prepare-Release.ps1#L17-L29) |

Dos detalles del PostgreSQL ya instalado:

- Si hay varios, se usa el de **mayor versión** que tenga `bin\psql.exe` y carpeta de datos
  ([setup_script.iss:320-331](../../Deploy/setup_script.iss#L320-L331)). Se buscan los registrados por EnterpriseDB y las
  carpetas de `C:\Program Files\PostgreSQL\` ([líneas 347-372](../../Deploy/setup_script.iss#L347-L372)).
- El puerto se lee de `postgresql.conf` y `postgresql.auto.conf`; si no aparece, del registro; si tampoco, 5432
  ([líneas 377-383](../../Deploy/setup_script.iss#L377-L383)).
- El PostgreSQL debe ser local: el instalador se conecta siempre a `localhost`
  ([líneas 410-420](../../Deploy/setup_script.iss#L410-L420), [línea 584](../../Deploy/setup_script.iss#L584)).

---

## 2. Qué instala el instalador

### 2.1 Tipos de instalación

| Tipo | Sitio web (IIS) | Programa de escritorio | Base y servicio ZKTeco |
|---|---|---|---|
| Sitio web y programa de escritorio | Sí | Sí | Sí |
| Sólo sitio web | Sí | No | Sí |
| Sólo programa de escritorio | No | Sí | Sí |
| Personalizada | A elegir (al menos uno de los dos) | A elegir | Sí, siempre |

Código: [setup_script.iss:98-107](../../Deploy/setup_script.iss#L98-L107), [líneas 1130-1139](../../Deploy/setup_script.iss#L1130-L1139).
Qué cambia entre "con sitio" y "sin sitio" está en [arquitectura, sección 4](01-arquitectura.md#4-formas-de-instalación).

Tareas opcionales del programa de escritorio: acceso directo en el escritorio, y abrirlo al iniciar sesión (desmarcada
por omisión) ([setup_script.iss:109-111](../../Deploy/setup_script.iss#L109-L111)).

### 2.2 Carpetas

La carpeta por omisión es `C:\Program Files\AttendanceSystem` (`{autopf}`)
([setup_script.iss:74](../../Deploy/setup_script.iss#L74)).

| Carpeta | Contenido | Permisos que agrega el instalador | Código |
|---|---|---|---|
| `Web\` | Sitio web | `IIS_IUSRS`: modificar (desde `configure_iis.bat`) | [setup_script.iss:127](../../Deploy/setup_script.iss#L127), [configure_iis.bat:85-88](../../Deploy/configure_iis.bat#L85-L88) |
| `Desktop\` | Programa de escritorio | `Desktop\logs` y `Desktop\Backups`: usuarios, modificar | [setup_script.iss:117-120](../../Deploy/setup_script.iss#L117-L120), [línea 130](../../Deploy/setup_script.iss#L130) |
| `Service\` | Servicio ZKTeco y SDK | — | [setup_script.iss:132-140](../../Deploy/setup_script.iss#L132-L140) |
| `Database\` | `init_db.sql`, `rename_db.sql` y, si el instalador instaló PostgreSQL, `postgres_superuser.txt` | El archivo de contraseña: sólo Administradores y SYSTEM | [setup_script.iss:147-149](../../Deploy/setup_script.iss#L147-L149), [líneas 855-862](../../Deploy/setup_script.iss#L855-L862) |
| `Tools\` | `enable_iis.ps1`, `configure_iis.bat` | — | [setup_script.iss:150-151](../../Deploy/setup_script.iss#L150-L151) |
| `Logs\` | Bitácoras del instalador | `IIS_IUSRS`: modificar (si existe al configurar IIS) | [setup_script.iss:114](../../Deploy/setup_script.iss#L114), [configure_iis.bat:99-102](../../Deploy/configure_iis.bat#L99-L102) |
| `Backups\` | Carpeta de respaldos | Usuarios, modificar, si se instala el programa de escritorio | [setup_script.iss:115-116](../../Deploy/setup_script.iss#L115-L116) |
| `Web\Backups\` | Respaldos del sitio | Usuarios, modificar, si conviven sitio y programa | [setup_script.iss:121](../../Deploy/setup_script.iss#L121) |

### 2.3 Orden de los pasos

Sección `[Run]` ([setup_script.iss:157-211](../../Deploy/setup_script.iss#L157-L211)):

| # | Paso | Cuándo | Código |
|---|---|---|---|
| 0 | Detener el servicio ZKTeco, el sitio y su grupo de aplicaciones; esperar hasta 10 s a que IIS suelte `aspnetcorev2_inprocess.dll` y, si no, terminar `w3wp.exe` y `AttendanceSystem.Blazor.Server.exe` | Antes de copiar archivos | [líneas 1330-1358](../../Deploy/setup_script.iss#L1330-L1358), [1296-1327](../../Deploy/setup_script.iss#L1296-L1327) |
| 1 | Copiar archivos y registrar `zkemkeeper.dll` (32 bits) en `Service\` | Siempre | [líneas 123-140](../../Deploy/setup_script.iss#L123-L140) |
| 2 | Instalar PostgreSQL en modo desatendido, puerto 5432, con contraseña generada | Sólo si no se detectó ninguno | [líneas 158-161](../../Deploy/setup_script.iss#L158-L161) |
| 3 | Configurar base y archivos de configuración (sección 3 y 4) | Antes de registrar el servicio | [líneas 818-913](../../Deploy/setup_script.iss#L818-L913) |
| 4 | Registrar el servicio `AttendanceSystem.ZKTeco.Service` (inicio automático) e iniciarlo | Siempre | [líneas 167-176](../../Deploy/setup_script.iss#L167-L176) |
| 5 | Regla de firewall `AttendanceSystem Web` (TCP 80 y 8081) | Con sitio web | [líneas 178-184](../../Deploy/setup_script.iss#L178-L184) |
| 6 | Habilitar IIS | Con sitio web | [líneas 186-190](../../Deploy/setup_script.iss#L186-L190) |
| 7 | Instalar o reparar el Hosting Bundle | Con sitio web, si falta el módulo o no está registrado en IIS | [líneas 192-195](../../Deploy/setup_script.iss#L192-L195), [537-551](../../Deploy/setup_script.iss#L537-L551) |
| 8 | Crear el grupo de aplicaciones y el sitio en IIS | Con sitio web | [líneas 197-199](../../Deploy/setup_script.iss#L197-L199) |
| 9 | Regla de firewall `AttendanceSystem ADMS` (TCP 18373) y verificación final | Siempre | [líneas 201-207](../../Deploy/setup_script.iss#L201-L207) |
| 10 | Arrancar el sitio y su grupo de aplicaciones | Después de instalar | [líneas 1359-1368](../../Deploy/setup_script.iss#L1359-L1368) |
| 11 | Ofrecer abrir el programa de escritorio (como el usuario que instaló, no como administrador) | Con programa de escritorio | [líneas 209-211](../../Deploy/setup_script.iss#L209-L211) |

---

## 3. Base de datos durante la instalación

### 3.1 Cuándo se crea

La base se configura cuando la carpeta no tiene una instalación configurada (no existe `Web\appsettings.Production.json`
ni `Desktop\appsettings.json`), o cuando se pide una base nueva
([setup_script.iss:616-621](../../Deploy/setup_script.iss#L616-L621), [línea 1202](../../Deploy/setup_script.iss#L1202)).
En cualquier otro caso se conservan la cadena de conexión y la `ApiKey` existentes
([líneas 829-849](../../Deploy/setup_script.iss#L829-L849)).

### 3.2 Contraseña de `postgres`

| Situación | De dónde sale | Código |
|---|---|---|
| No había PostgreSQL | El instalador genera una (16 bytes en hexadecimal) y la guarda en `Database\postgres_superuser.txt` | [líneas 1215-1223](../../Deploy/setup_script.iss#L1215-L1223), [855-862](../../Deploy/setup_script.iss#L855-L862) |
| Había PostgreSQL y es la primera instalación en la carpeta | Se pide en una página del asistente y se prueba antes de seguir | [líneas 1082-1093](../../Deploy/setup_script.iss#L1082-L1093), [1171-1189](../../Deploy/setup_script.iss#L1171-L1189) |
| Base nueva en una instalación existente | La guardada en `postgres_superuser.txt` si sigue funcionando; si no, se pide | [líneas 1113-1117](../../Deploy/setup_script.iss#L1113-L1117), [1148-1155](../../Deploy/setup_script.iss#L1148-L1155) |

La contraseña se pasa a `psql` por la variable `PGPASSWORD` y se borra después
([líneas 422-430](../../Deploy/setup_script.iss#L422-L430)).

### 3.3 Qué crea `init_db.sql`

Se ejecuta como `postgres` con `ON_ERROR_STOP`, después de esperar hasta 60 s (30 intentos cada 2 s) a que PostgreSQL
acepte conexiones ([setup_script.iss:872-902](../../Deploy/setup_script.iss#L872-L902)). Es idempotente
([init_db.sql](../../Deploy/init_db.sql)):

1. Usuario `attendancesystem_user` con permiso de inicio de sesión; su contraseña se **fija de nuevo** en cada
   ejecución ([init_db.sql:9-12](../../Deploy/init_db.sql#L9-L12)).
2. Base `AttendanceSystem`, propiedad de ese usuario ([líneas 15-19](../../Deploy/init_db.sql#L15-L19)).
3. El esquema `public` pasa a ser del usuario, porque la restauración de respaldos lo borra y lo recrea
   ([líneas 23-28](../../Deploy/init_db.sql#L23-L28)).
4. Esquema `hangfire`, del usuario ([líneas 30-35](../../Deploy/init_db.sql#L30-L35)).

Las tablas no las crea el instalador: las crean las migraciones al arrancar el sitio (o el programa en modo
`Standalone`), ver [arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento).

La contraseña del usuario de la aplicación (24 bytes en hexadecimal) y la `ApiKey` (32 bytes) se generan en esa misma
ejecución ([setup_script.iss:1225-1232](../../Deploy/setup_script.iss#L1225-L1232)). La cadena de conexión queda
`Host=localhost;Port=<puerto>;Database=AttendanceSystem;Username=attendancesystem_user;Password=<generada>;`
([línea 584](../../Deploy/setup_script.iss#L584)).

### 3.4 Base que ya existe

- **Primera instalación sobre una base `AttendanceSystem` existente** (por ejemplo, tras desinstalar): el asistente
  ofrece usarla con sus datos o crear una vacía ([setup_script.iss:1097-1109](../../Deploy/setup_script.iss#L1097-L1109)).
- **Base nueva** (opción del asistente o parámetro `/NEWDATABASE`): la base actual **no se borra**, se renombra a
  `AttendanceSystem_anterior_AAAAMMDD_hhmmss`. Antes se impiden conexiones nuevas y se cierran las abiertas, esperando
  hasta 10 s por cada una ([setup_script.iss:713-737](../../Deploy/setup_script.iss#L713-L737),
  [rename_db.sql](../../Deploy/rename_db.sql)). Si no se puede renombrar, no se cambia nada
  ([setup_script.iss:888-893](../../Deploy/setup_script.iss#L888-L893)). Si la instalación usaba otra base (por ejemplo
  `AttendanceSystemTest`), ésa no se toca ([líneas 720-725](../../Deploy/setup_script.iss#L720-L725)).

---

## 4. Archivos de configuración que escribe

| Parte | Archivo | Plantilla | Código |
|---|---|---|---|
| Sitio web | `Web\appsettings.Production.json` | [appsettings.Web.Production.template.json](../../Deploy/appsettings.Web.Production.template.json) | [setup_script.iss:601-604](../../Deploy/setup_script.iss#L601-L604) |
| Programa de escritorio | `Desktop\appsettings.json` | [appsettings.Desktop.template.json](../../Deploy/appsettings.Desktop.template.json) | [líneas 606-609](../../Deploy/setup_script.iss#L606-L609) |
| Servicio ZKTeco | `Service\appsettings.Production.json` | [appsettings.Service.Production.template.json](../../Deploy/appsettings.Service.Production.template.json) | [líneas 611-614](../../Deploy/setup_script.iss#L611-L614) |

- Cada archivo **se escribe sólo si no existe**: una actualización conserva lo que se haya cambiado a mano
  ([setup_script.iss:796-816](../../Deploy/setup_script.iss#L796-L816)). Por eso los archivos de la publicación con esos
  nombres no se copian ([líneas 124-133](../../Deploy/setup_script.iss#L124-L133)).
- Marcadores que se reemplazan: `__DB_CONNECTION__`, `__API_KEY__`, `__PG_BIN__` (la carpeta `bin` del PostgreSQL
  usado, para `pg_dump` y `pg_restore`) y `__STANDALONE__` ([líneas 740-763](../../Deploy/setup_script.iss#L740-L763)).
- **`Hosting:Standalone`** del programa de escritorio se recalcula en cada instalación: `true` si el sitio web no está
  instalado en esa carpeta, `false` si sí ([líneas 765-793](../../Deploy/setup_script.iss#L765-L793),
  [líneas 809-815](../../Deploy/setup_script.iss#L809-L815)).
- La plantilla del sitio web también fija `Serilog:WriteTo:3:Args:connectionString`. Ver la sección 13, punto 1.

---

## 5. IIS, servicio y firewall

### 5.1 IIS

[enable_iis.ps1](../../Deploy/enable_iis.ps1) habilita el servidor web con documentos por omisión, contenido estático,
errores HTTP, registro, filtrado de peticiones, compresión estática, **WebSockets** (necesario para Blazor Server) y la
consola de administración. Usa `Install-WindowsFeature` en Windows Server y `Enable-WindowsOptionalFeature` en las demás
versiones ([enable_iis.ps1:13-42](../../Deploy/enable_iis.ps1#L13-L42)).

[configure_iis.bat](../../Deploy/configure_iis.bat), con la carpeta `Web`, la bitácora y el puerto ADMS:

1. Crea el grupo de aplicaciones `AttendanceSystem` sin código administrado y en canalización integrada, si no existe
   ([configure_iis.bat:35-46](../../Deploy/configure_iis.bat#L35-L46)). No fija identidad, así que usa la de IIS por
   omisión.
2. **Borra el sitio `AttendanceSystem` si existe y lo vuelve a crear** ([líneas 48-53](../../Deploy/configure_iis.bat#L48-L53)).
3. **Detiene el sitio `Default Web Site`** ([líneas 55-56](../../Deploy/configure_iis.bat#L55-L56)).
4. Crea el sitio en el puerto 80; si falla, en el 8081 ([líneas 60-72](../../Deploy/configure_iis.bat#L60-L72)).
5. Agrega el enlace del puerto ADMS (18373) ([líneas 77-83](../../Deploy/configure_iis.bat#L77-L83)).
6. Da permiso de modificar a `IIS_IUSRS` sobre `Web`, `Backups` y `Logs` ([líneas 85-102](../../Deploy/configure_iis.bat#L85-L102)).

`web.config` usa el modo *in-process* y un tiempo de arranque de 3600 s, para que IIS no corte una migración larga
([web.config:8-12](../../src/Presentation/AttendanceSystem.Blazor.Server/web.config#L8-L12)).

### 5.2 Servicio ZKTeco

- Nombre `AttendanceSystem.ZKTeco.Service`, nombre visible "Attendance System ZKTeco Service", inicio automático
  ([setup_script.iss:167-176](../../Deploy/setup_script.iss#L167-L176)). Corre con la cuenta por omisión de `sc create`
  (LocalSystem).
- `zkemkeeper.dll` se registra como COM de 32 bits desde `Service\`, junto a sus dependencias, para no depender de las
  DLL que otros programas de ZKTeco dejan en `SysWOW64` ([setup_script.iss:135-140](../../Deploy/setup_script.iss#L135-L140)).
  Al desinstalar se quita el registro.

### 5.3 Firewall

| Regla | Puertos | Cuándo | Código |
|---|---|---|---|
| `AttendanceSystem Web` | TCP 80 y 8081, entrada | Con sitio web | [setup_script.iss:181-184](../../Deploy/setup_script.iss#L181-L184) |
| `AttendanceSystem ADMS` | TCP 18373, entrada | Siempre | [setup_script.iss:204-207](../../Deploy/setup_script.iss#L204-L207) |

El puerto 5001 del servicio ZKTeco no se abre: el servicio escucha sólo en `localhost` por omisión
([relojes, sección 2.2](04-dispositivos.md#22-configuración)).

---

## 6. Verificación final y bitácoras del instalador

Al terminar, el instalador revisa ([setup_script.iss:939-983](../../Deploy/setup_script.iss#L939-L983)):

- si la base quedó configurada;
- si el programa de escritorio tiene su archivo de configuración;
- con sitio web: si IIS está habilitado, si el módulo de ASP.NET Core está instalado y registrado, y si existe el sitio;
- si el servicio ZKTeco llega a `RUNNING` en 10 s.

Cualquier problema se muestra en un mensaje y en la pantalla final, con la bitácora que corresponde
([líneas 1235-1260](../../Deploy/setup_script.iss#L1235-L1260)). La pantalla final también avisa dónde quedó la
contraseña de `postgres` y, si se creó una base nueva, cómo se llama la anterior.

| Bitácora | Qué contiene |
|---|---|
| `Logs\db_setup.log` | Salida de `psql`: conexión, `init_db.sql`, `rename_db.sql` |
| `Logs\enable_iis.log` | Habilitación de IIS |
| `Logs\dotnet_hosting.log` | Instalación del Hosting Bundle |
| `Logs\iis_setup.log` | `configure_iis.bat` |
| `%TEMP%` | Bitácora del instalador de PostgreSQL ([línea 867](../../Deploy/setup_script.iss#L867)) |

---

## 7. Actualizar, reinstalar, agregar partes y desinstalar

| Operación | Cómo la reconoce | Qué pasa | Código |
|---|---|---|---|
| Actualizar | Versión del instalador mayor que la instalada | Reemplaza archivos; conserva configuración y datos | [setup_script.iss:1000-1008](../../Deploy/setup_script.iss#L1000-L1008), [1043-1047](../../Deploy/setup_script.iss#L1043-L1047) |
| Reinstalar | Misma versión | Igual que actualizar (reparación) | [líneas 1048-1052](../../Deploy/setup_script.iss#L1048-L1052) |
| Instalar una versión anterior | Versión menor que la instalada | **Se rechaza**, salvo con `/ALLOWDOWNGRADE` | [líneas 1010-1023](../../Deploy/setup_script.iss#L1010-L1023) |
| Agregar el sitio o el programa | Componente nuevo marcado | Escribe su configuración con la conexión y la `ApiKey` existentes y recalcula `Standalone` | [líneas 657-671](../../Deploy/setup_script.iss#L657-L671), [796-816](../../Deploy/setup_script.iss#L796-L816) |
| Quitar el sitio o el programa | Componente instalado desmarcado | **No se permite**: hay que desinstalar y volver a instalar | [líneas 1128-1139](../../Deploy/setup_script.iss#L1128-L1139) |
| Base nueva | Opción del asistente o `/NEWDATABASE` | Sección 3.4 | [líneas 997-998](../../Deploy/setup_script.iss#L997-L998) |

Las versiones se comparan por sus cuatro primeros números; un sufijo como `-beta.1` no cuenta, así que `2.3.0-beta.1` y
`2.3.0` se toman como la misma versión ([setup_script.iss:466-502](../../Deploy/setup_script.iss#L466-L502)).

**Instalación silenciosa** (`/SILENT`, `/VERYSILENT`): no puede pedir la contraseña de `postgres`, así que sólo funciona
si no hace falta, es decir, si no había PostgreSQL, si es una actualización sin base nueva, o si se pide base nueva y sirve la contraseña guardada en `postgres_superuser.txt`
([setup_script.iss:1204-1213](../../Deploy/setup_script.iss#L1204-L1213)).

**Desinstalar** borra el sitio y el grupo de aplicaciones de IIS, las dos reglas de firewall y el servicio
([setup_script.iss:218-227](../../Deploy/setup_script.iss#L218-L227)), además de los archivos que copió. No están en
esa lista, y por lo tanto quedan:

- la base de datos y PostgreSQL;
- los archivos de configuración y `postgres_superuser.txt`, que el instalador escribió por código y no copió;
- las bitácoras y los respaldos;
- IIS y el Hosting Bundle.

Al volver a instalar, la base existente se puede usar (sección 3.4).

---

## 8. Instalación manual

Los mismos pasos del instalador, para quien no pueda usarlo. Las publicaciones ya compiladas de cada parte están en
cada versión de GitHub como `AttendanceSystem_Web_v<versión>.zip`, `AttendanceSystem_Desktop_v<versión>.zip` y
`AttendanceSystem_ZKTecoService_v<versión>.zip` ([release.yml:111-113](../../.github/workflows/release.yml#L111-L113)).
Los comandos van en PowerShell como administrador; `C:\AttendanceSystem` es un ejemplo de carpeta.

**1. PostgreSQL 14 o posterior** instalado y en marcha. Crear el usuario y la base con el mismo script del instalador:

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -w -v ON_ERROR_STOP=1 -h localhost -U postgres -d postgres -v app_password=<contraseña> -f .\Deploy\init_db.sql
```

(`-w` no pide contraseña: hay que fijar antes `$env:PGPASSWORD` con la de `postgres`.)

**2. Servicio ZKTeco.** Descomprimir en `C:\AttendanceSystem\Service`, crear `appsettings.Production.json` a partir de su
plantilla con una `ApiKey` larga y aleatoria, y registrar el SDK con el `regsvr32` de 32 bits:

```powershell
C:\Windows\SysWOW64\regsvr32.exe /s C:\AttendanceSystem\Service\zkemkeeper.dll
```

```powershell
sc.exe create AttendanceSystem.ZKTeco.Service binPath= "C:\AttendanceSystem\Service\AttendanceSystem.ZKTeco.Service.exe" start= auto displayname= "Attendance System ZKTeco Service"
```

```powershell
sc.exe start AttendanceSystem.ZKTeco.Service
```

**3. Sitio web** (si se usa):

```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy\enable_iis.ps1
```

Después, instalar el ASP.NET Core Hosting Bundle de .NET 10 (si IIS se habilitó después del Hosting Bundle, hay que
repararlo), descomprimir el sitio en `C:\AttendanceSystem\Web` y crear `appsettings.Production.json` con la cadena de
conexión, `ZKTecoService:ApiKey` (la misma del servicio) y `Backup:PostgresBinPath`. **No copie el bloque `Serilog` de
la plantilla**: sin un `appsettings.json` con cuatro sinks, el sitio no arranca (sección 13, punto 1). Por último:

```powershell
.\Deploy\configure_iis.bat "C:\AttendanceSystem\Web" "C:\AttendanceSystem\iis_setup.log" 18373
```

**4. Programa de escritorio** (si se usa): descomprimir en `C:\AttendanceSystem\Desktop`, crear `appsettings.json` a
partir de su plantilla con `Hosting:Standalone` en `true` si no hay sitio web, y dar a los usuarios permiso de escritura
sobre `Desktop\logs` y la carpeta de respaldos.

**5. Firewall:**

```powershell
netsh advfirewall firewall add rule name="AttendanceSystem ADMS" dir=in action=allow protocol=TCP localport=18373
```

```powershell
netsh advfirewall firewall add rule name="AttendanceSystem Web" dir=in action=allow protocol=TCP localport=80,8081
```

Al arrancar por primera vez, el sitio (o el programa en modo `Standalone`) aplica las migraciones y crea el usuario
`admin` ([arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento)).

---

## 9. Equipo de desarrollo

| Necesita | Por qué | Código |
|---|---|---|
| SDK de .NET 10 | Todos los proyectos son `net10.0`; el WPF, `net10.0-windows10.0.17763.0` | [Blazor.Server.csproj:4](../../src/Presentation/AttendanceSystem.Blazor.Server/AttendanceSystem.Blazor.Server.csproj#L4), [WPF.csproj:5](../../src/Presentation/AttendanceSystem.WPF/AttendanceSystem.WPF.csproj#L5) |
| Windows | El WPF y el servicio ZKTeco (COM de 32 bits) sólo corren en Windows; `EnableWindowsTargeting` permite compilar en otros sistemas | [Directory.Build.props:7](../../Directory.Build.props#L7), [ZKTeco.Service.csproj:8](../../src/Presentation/AttendanceSystem.ZKTeco.Service/AttendanceSystem.ZKTeco.Service.csproj#L8) |
| PostgreSQL 14 o posterior | Base de la aplicación y de algunas pruebas | Sección 3 |

Pasos:

1. Crear el usuario y la base con `init_db.sql` (sección 8, paso 1).
2. Copiar los ejemplos de configuración, que están en el repositorio porque los `appsettings.json` reales están en
   `.gitignore` ([.gitignore:92-95](../../.gitignore#L92-L95)):
   - `src/Presentation/AttendanceSystem.Blazor.Server/appsettings.example.json` → `appsettings.json`
   - `src/Presentation/AttendanceSystem.WPF/appsettings.example.json` → `appsettings.json`
   - `src/Presentation/AttendanceSystem.ZKTeco.Service/appsettings.Example.json` → `appsettings.json`

   y poner en los tres la cadena de conexión y la misma `ApiKey`. El ejemplo del sitio no trae `ZKTecoService:ApiKey`
   ([appsettings.example.json:47-52](../../src/Presentation/AttendanceSystem.Blazor.Server/appsettings.example.json#L47-L52)):
   hay que agregarla.
3. Registrar el SDK de ZKTeco para el servicio:
   `C:\Windows\SysWOW64\regsvr32.exe src\Infrastructure\AttendanceSystem.ZKTeco\lib\zkemkeeper.dll`. Si `regsvr32`
   termina con código 3, la causa probable son DLL de otro SDK en `SysWOW64` (ver el comentario de
   [setup_script.iss:135-139](../../Deploy/setup_script.iss#L135-L139)).
4. Arrancar el sitio con su perfil: `http://localhost:8080` (interfaz) y `http://0.0.0.0:8081`, entorno `Development`
   ([launchSettings.json](../../src/Presentation/AttendanceSystem.Blazor.Server/Properties/launchSettings.json)). En
   desarrollo se activa la redirección a HTTPS ([Program.cs:185-188](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L185-L188)).
   El sitio aplica las migraciones al arrancar.
5. Pruebas: `dotnet test AttendanceSystem.sln`. Las de PostgreSQL usan `TEST_POSTGRES_CONNECTION` o, sin ella,
   `localhost:5432` con el usuario `attendanceuser` y contraseña `AttendancePass`
   ([PostgresTestDatabaseFixture.cs:31-45](../../tests/AttendanceSystem.Infrastructure.Tests/Persistence/PostgresTestDatabaseFixture.cs#L31-L45)).

En desarrollo, el puerto ADMS no lo abre el sitio: lo define `applicationUrl`. Para recibir un reloj ADMS hay que
apuntarlo al 8081, o arrancar el WPF en modo `Standalone`, que sí escucha en `AdmsPort`.

---

## 10. Referencia de `appsettings`

Cada clave que lee el código. "Instalador" indica si la escribe el instalador. Donde no se indica otra cosa, la clave se
lee al arrancar: un cambio exige reiniciar el proceso (en IIS, reciclar el grupo de aplicaciones).

**De dónde se lee.** El sitio y el servicio ZKTeco usan `WebApplication.CreateBuilder`: `appsettings.json`,
`appsettings.<entorno>.json` y variables de entorno, en ese orden de prioridad creciente. Sin `ASPNETCORE_ENVIRONMENT`
el entorno es `Production`. El programa de escritorio usa `appsettings.json` de su carpeta
([App.xaml.cs:52-70](../../src/Presentation/AttendanceSystem.WPF/App.xaml.cs#L52-L70),
[WpfBackendHost.cs:34-46](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L34-L46)).

### 10.1 Sitio web y programa de escritorio

Las dos partes comparten la capa de infraestructura, así que leen las mismas claves salvo donde se indica.

| Clave | Por omisión | Instalador | Efecto | Código |
|---|---|---|---|---|
| `ConnectionStrings:AttendanceDb` | — (obligatoria) | Sí | Base de la aplicación | [InfrastructureServiceExtensions.cs:34](../../src/Infrastructure/AttendanceSystem.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs#L34), [App.xaml.cs:74-77](../../src/Presentation/AttendanceSystem.WPF/App.xaml.cs#L74-L77) |
| `ConnectionStrings:HangfireDb` | En el WPF, `AttendanceDb`; en el sitio, ninguno | Sí | Almacenamiento de Hangfire | [Program.cs:146](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L146), [WpfBackendHost.cs:80](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L80) |
| `Hosting:Standalone` | `false` | Sí (WPF) | Sólo WPF: aplica migraciones y recibe ADMS ([arquitectura, sección 4](01-arquitectura.md#4-formas-de-instalación)) | [WpfBackendHost.cs:31](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L31), [App.xaml.cs:71](../../src/Presentation/AttendanceSystem.WPF/App.xaml.cs#L71) |
| `ZKTecoService:Url` | `http://localhost:5001` | Sí | Servicio ZKTeco | [InfrastructureServiceExtensions.cs:146](../../src/Infrastructure/AttendanceSystem.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs#L146) |
| `ZKTecoService:ApiKey` (o `ApiKey`) | ninguno | Sí | Cabecera `x-api-key` hacia el servicio. En el sitio, también autoriza `POST /api/configuration/reload-jobs` y `POST /api/external-logs/receive` | [InfrastructureServiceExtensions.cs:152](../../src/Infrastructure/AttendanceSystem.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs#L152), [ConfigurationController.cs:38](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ConfigurationController.cs#L38), [ExternalLogsController.cs:47](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ExternalLogsController.cs#L47) |
| `ExternalLogs:ApiKey` | ninguno | No | Sitio: clave de `api/external-logs` sólo si no hay `ApiKey` ni `ZKTecoService:ApiKey` | [ExternalLogsController.cs:47](../../src/Presentation/AttendanceSystem.Blazor.Server/Controllers/ExternalLogsController.cs#L47) |
| `ZKTecoService:Realtime:Enabled` | `false` | No | Sitio: tiempo real por SDK ([relojes, 2.7](04-dispositivos.md#27-tiempo-real-por-sdk)) | [ZKTecoRealtimeSubscriber.cs:21](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/ZKTecoRealtime/ZKTecoRealtimeSubscriber.cs#L21) |
| `Backup:PostgresBinPath` (o `DatabaseBackup:PostgresBinPath`) | ninguno | Sí | Carpeta de `pg_dump` y `pg_restore` | [BackupService.cs:2010](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L2010) |
| `Backup:DumpStallTimeoutSeconds` | 600 | No | Respaldo sin avance por más de este tiempo se aborta | [BackupService.cs:50](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L50) |
| `Backup:RestoreStallTimeoutSeconds` | 900 | No | Lo mismo para la restauración | [BackupService.cs:51](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L51) |
| `Backup:DrainTimeoutSeconds` | 60 (mínimo 1) | No | Espera a que terminen las tareas antes de restaurar | [BackupService.cs:54](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L54) |
| `Backup:KeepLastNBackups` | 10 | No | Respaldos que se conservan | [BackupService.cs:1109](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/BackupService.cs#L1109) |
| `Maintenance:PollIntervalSeconds` | 5 | Sí (WPF) | Cada cuánto se revisa si hay mantenimiento o restauración en curso | [RestoreStateService.cs:51](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/RestoreStateService.cs#L51) |
| `Database:StartupConnectAttempts` | 6 (mínimo 1) | No | Intentos de conexión al arrancar | [StartupMigrationService.cs:61](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/StartupMigrationService.cs#L61) |
| `Database:StartupConnectRetrySeconds` | 2 | No | Espera entre intentos | [StartupMigrationService.cs:62](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/StartupMigrationService.cs#L62) |
| `Database:MigrationTimeoutMinutes` | 30 (mínimo 1) | No | Tiempo límite de los comandos de migración | [StartupMigrationService.cs:70](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/StartupMigrationService.cs#L70) |
| `Database:MigrationLockTimeoutSeconds` | 120 (mínimo 1) | No | Espera del candado de migración | [SchemaMigrator.cs:20](../../src/Infrastructure/AttendanceSystem.Infrastructure/Persistence/SchemaMigrator.cs#L20) |
| `Database:StartupMigrationBudgetMinutes` | 50 (mínimo 1) | No | Tiempo total para migrar al arrancar; debe ser menor que el `startupTimeLimit` de IIS (3600 s) | [SchemaMigrator.cs:36](../../src/Infrastructure/AttendanceSystem.Infrastructure/Persistence/SchemaMigrator.cs#L36), [web.config:8-12](../../src/Presentation/AttendanceSystem.Blazor.Server/web.config#L8-L12) |
| `AttendanceLocks:TimeoutSeconds` | 30 | No | Espera del candado de recálculo por empleado | [InfrastructureServiceExtensions.cs:109](../../src/Infrastructure/AttendanceSystem.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs#L109) |
| `Consolidation:QuietPeriod` | 00:00:30 | No | Espera desde el último marcaje de un empleado antes de recalcular | [ConsolidationOptions.cs:11](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L11) |
| `Consolidation:MaxDelay` | 00:05:00 | No | Antigüedad máxima de una solicitud antes de procesarla | [línea 14](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L14) |
| `Consolidation:Lease` | 00:15:00 | No | Tiempo que una solicitud reclamada es exclusiva | [línea 17](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L17) |
| `Consolidation:BatchSize` | 100 | No | Solicitudes por ciclo | [línea 20](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L20) |
| `Consolidation:MaxAttempts` | 5 | No | Intentos por solicitud | [línea 21](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L21) |
| `Consolidation:BaseBackoff` | 00:00:30 | No | Espera base entre intentos | [línea 22](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L22) |
| `Consolidation:PollInterval` | 00:00:15 | No | Sondeo de respaldo | [línea 25](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L25) |
| `Consolidation:FinishedRequestRetention`, `Consolidation:ProcessedOutboxRetention` | 30 días | No | Conservación de solicitudes terminadas y mensajes procesados | [líneas 27-28](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L27-L28) |
| `Consolidation:PurgeInterval` | 6 h | No | Cada cuánto se purgan | [línea 29](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L29) |
| `Consolidation:HealthCheckInterval` | 00:00:30 | No | Revisión de salud de la cola | [línea 32](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L32) |
| `Consolidation:AlertPendingAge` | 00:10:00 | No | Alerta si la solicitud pendiente más vieja supera esta antigüedad | [línea 35](../../src/Core/AttendanceSystem.Application/Features/Attendance/Consolidation/ConsolidationOptions.cs#L35) |
| `InitialAdmin:Password` (o la variable `INITIAL_ADMIN_PASSWORD`) | se genera una | No | Contraseña del usuario `admin` si no existe | [IdentityDataSeeder.cs:57](../../src/Infrastructure/AttendanceSystem.Infrastructure/Persistence/IdentityDataSeeder.cs#L57) |
| `ShutdownTimeoutSeconds` | 30 | No | Sitio: espera al detenerse | [Program.cs:59-63](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L59-L63) |
| `HTTPS_PORT` | ninguno | No | Sitio: con valor (o en desarrollo) redirige a HTTPS | [Program.cs:185-188](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L185-L188) |
| `Serilog` | — | Parcial (sección 13) | Sitio: niveles y sinks adicionales; los archivos por área los agrega el código. WPF: toda su bitácora | [LoggingConfiguration.cs:27-128](../../src/Infrastructure/AttendanceSystem.Infrastructure/Logging/LoggingConfiguration.cs#L27-L128), [WpfBackendHost.cs:59-66](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L59-L66) |
| `SendGrid:*` | — | No | **Sin efecto**: el servicio que la lee no está registrado; el correo usa SMTP de la configuración en la base | [InfrastructureServiceExtensions.cs:124](../../src/Infrastructure/AttendanceSystem.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs#L124), [SendGridEmailService.cs:30-32](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/SendGridEmailService.cs#L30-L32) |

Bitácoras del sitio. Además de lo que diga `Serilog`, el código siempre escribe estos archivos, relativos a la carpeta
del sitio ([LoggingConfiguration.cs:48-125](../../src/Infrastructure/AttendanceSystem.Infrastructure/Logging/LoggingConfiguration.cs#L48-L125)):

| Archivo | Contenido | Se conservan |
|---|---|---|
| `logs/adms/adms-*.log` | ADMS | 30 archivos |
| `logs/database/database-*.log` | EF Core, Npgsql, persistencia | 15 |
| `logs/attendance/attendance-processing-*.log` | Cálculo de asistencia | 60 |
| `logs/zkteco/zkteco-comm-*.log` | Cliente del servicio ZKTeco | 30 |
| `logs/system/system-*.log` | Lo demás | 30 |
| `logs/errors/errors-*.log` | Errores de cualquier área | 90 |
| `logs/bootstrap/bootstrap-*.txt` | Arranque, antes de leer la configuración | 7 ([Program.cs:39-44](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L39-L44)) |

Todos rotan por día o a los 10 MB (20 MB los de base de datos).

### 10.2 Servicio ZKTeco

| Clave | Por omisión | Instalador | Efecto | Código |
|---|---|---|---|---|
| `GrpcPort` | 5001 | Sí | Puerto gRPC | [Program.cs:142](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L142), [Worker.cs:43](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Worker.cs#L43) |
| `GrpcBindLocalOnly` | `true` | No | Sólo `localhost` | [Program.cs:143](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L143) |
| `ApiKey` (o `ZKTecoService:ApiKey`) | — (obligatoria) | Sí | Clave que exige a cada llamada | [ApiKeyAuthInterceptor.cs:19-25](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Interceptors/ApiKeyAuthInterceptor.cs#L19-L25) |
| `ShutdownTimeoutSeconds` | 15 | No | Espera al detenerse | [Program.cs:78-82](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L78-L82) |
| `Realtime:HeartbeatInterval`, `Realtime:ReconnectDelays`, `Realtime:ChannelCapacity` | 30 s; 5, 10, 30, 60 s; 10 000 | No | Escucha en tiempo real | [RealtimeModels.cs:25-38](../../src/Infrastructure/AttendanceSystem.ZKTeco/Realtime/RealtimeModels.cs#L25-L38) |
| `Serilog` | — | No | Niveles y sinks adicionales; los archivos `logs\zkteco\` y `logs\errors\` los agrega el código | [Program.cs:91-114](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L91-L114) |

### 10.3 Claves que el instalador escribe y el código no lee

`AllowedHosts` (sitio y servicio) la usa ASP.NET Core para filtrar el encabezado `Host`; con `*` no filtra. La sección
`Logging` de la plantilla del servicio no tiene efecto sobre Serilog, que reemplaza el registro de .NET
([Program.cs:91](../../src/Presentation/AttendanceSystem.ZKTeco.Service/Program.cs#L91)).

---

## 11. Configuración guardada en la base

Una sola fila en `SystemConfigurations`, con el identificador fijo `11111111-1111-1111-1111-111111111111`
([SystemConfiguration.cs:9](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L9)).
Se edita en la pantalla de configuración del sitio o del programa; guardarla exige el permiso `SettingsManage`
([UpdateSystemConfigurationCommand.cs:10](../../src/Core/AttendanceSystem.Application/Features/Configuration/Commands/UpdateSystemConfiguration/UpdateSystemConfigurationCommand.cs#L10)).
Si no existe, se crea con los valores por omisión al guardar por primera vez
([líneas 79-85](../../src/Core/AttendanceSystem.Application/Features/Configuration/Commands/UpdateSystemConfiguration/UpdateSystemConfigurationCommand.cs#L79-L85)).

Valores por omisión en [SystemConfiguration.cs:84-121](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L84-L121).
Los campos de [UpdateSettings](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L123-L177)
se guardan **sin validar** (puertos, horas, correos).

### 11.1 Empresa, periodos y zona

| Campo | Por omisión | Efecto | Quién lo lee |
|---|---|---|---|
| `CompanyName`, `CompanyLogo` | "Mi Empresa", sin logo | Encabezado de reportes y remitente del correo | Reportes, `SendDailyReportCommand`, `SmtpEmailService` |
| `WorkPeriodMode` | Semanal | Periodo de pago: semanal, quincenal o mensual | `WorkPeriodCalculator`, `WorkPeriodHelper` |
| `WeeklyStartDay` | Lunes | Inicio de la semana; también lo usa el cálculo diario ([ProcessDailyAttendanceCommandHandler.cs:123](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L123)) | Cálculo y reportes |
| `FortnightFirstDay`, `FortnightSecondDay` | 1 y 16 | Inicio de cada quincena | `WorkPeriodCalculator` |
| `MonthlyStartDay` | 1 | Inicio del mes de pago | `WorkPeriodCalculator` |
| `DefaultTimeZoneId` | `America/Mexico_City` | Zona de los relojes sin zona propia y de las tareas programadas ([relojes, 5.1](04-dispositivos.md#51-de-qué-zona-es-un-reloj)) | `AuthoritativeTimeZoneResolver` |

### 11.2 Tareas automáticas

Se programan en Hangfire al guardar y al arrancar, a la hora indicada en la zona `DefaultTimeZoneId`, todos los días
([ScheduledJobsSynchronizer.cs:33-53](../../src/Core/AttendanceSystem.Application/Features/Configuration/Services/ScheduledJobsSynchronizer.cs#L33-L53),
[UpdateSystemConfigurationCommand.cs:166-169](../../src/Core/AttendanceSystem.Application/Features/Configuration/Commands/UpdateSystemConfiguration/UpdateSystemConfigurationCommand.cs#L166-L169)).
El cálculo diario se programa siempre.

| Campos | Por omisión | Efecto |
|---|---|---|
| `IsAutoDownloadEnabled`, `AutoDownloadTime` | Apagada | Descarga de todos los relojes |
| `IsAutoBackupEnabled`, `AutoBackupTime` | Apagado | Respaldo de la base |
| `IsAutoReportEnabled`, `AutoReportTime`, `AutoReportEmails`, `AutoReportForToday` | Apagado | Reporte diario por correo, del día actual o del anterior |
| `BackupDirectory` | `Backups` junto al programa que creó la fila | Carpeta de respaldos ([SystemConfiguration.cs:98](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L98)) |

### 11.3 Correo y alertas

| Campos | Por omisión | Efecto |
|---|---|---|
| `AreAlertsEnabled` | Apagado | Sin esto no se envían alertas ni corre el monitoreo de relojes ([relojes, 7](04-dispositivos.md#7-estado-en-línea-y-monitoreo)) |
| `AbsenceAlertEmails`, `LateAlertEmails`, `SystemFailureAlertEmails` | vacíos | Destinatarios por tipo de alerta |
| `SmtpHost`, `SmtpPort`, `SmtpUser`, `SmtpPassword`, `SmtpEnableSsl` | sin servidor, 587, SSL activado | Envío de correo; el remitente es `SmtpUser` ([SmtpEmailService.cs:30-58](../../src/Infrastructure/AttendanceSystem.Infrastructure/Services/SmtpEmailService.cs#L30-L58)). La contraseña se guarda en texto plano ([base de datos](02-base-de-datos.md)) |

### 11.4 ADMS y nube

| Campos | Por omisión | Efecto |
|---|---|---|
| `AdmsPort` | 18373 | Puerto en que el WPF `Standalone` recibe a los relojes, leído al arrancar ([WpfBackendHost.cs:116-134](../../src/Presentation/AttendanceSystem.WPF/Services/WpfBackendHost.cs#L116-L134)). En el sitio sólo se muestra en la bitácora: el puerto real es el enlace de IIS ([Program.cs:230](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L230)). **Cambiarlo no cambia el enlace de IIS ni la regla de firewall** ([setup_script.iss:33-35](../../Deploy/setup_script.iss#L33-L35)) |
| `IsCloudSyncEnabled`, `CloudDbConnectionString` | Apagada | Envío de checadas de sucursales externas a la base en la nube |

### 11.5 Cálculo y relojes

La comida, el toque doble y la cuarentena se documentan donde se usan: [motor de cálculo, comida](03-motor-de-calculo.md)
y [relojes, política de cuarentena](04-dispositivos.md#61-política). Éstos sí se validan al guardar
([SystemConfiguration.cs:186-243](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L186-L243)).

### 11.6 Campos sin efecto

Se muestran y se guardan, pero ningún otro código los lee. Se puede comprobar con:

```bash
grep -rn "LateTolerance\|StandardWorkHours\|AutoClearDevicesAfterDownload\|AutoDownloadOnlyToday\|BackupTimeoutMinutes" src --include=*.cs | grep -v "Migrations\|ModelSnapshot"
```

| Campo | Lo que parece hacer | Lo que hace en realidad |
|---|---|---|
| `LateTolerance` | Tolerancia de retardo global | Nada: la tolerancia es la de cada turno ([motor de cálculo](03-motor-de-calculo.md)) |
| `StandardWorkHours` | Jornada estándar | Nada |
| `AutoClearDevicesAfterDownload` | Borrar los relojes tras descargar | Nada: se usa la opción de cada reloj ([relojes, 2.5](04-dispositivos.md#25-descarga-de-marcajes)) |
| `AutoDownloadOnlyToday` | Descargar sólo el día actual | Nada |
| `BackupTimeoutMinutes` | Tiempo límite de `pg_dump` | Nada: el límite es `Backup:DumpStallTimeoutSeconds` (10.1) |

---

## 12. Problemas posibles y cómo resolverlos

| Síntoma | Causa según el código | Cómo comprobarlo | Qué hacer |
|---|---|---|---|
| El instalador se cierra al empezar: "requiere PostgreSQL 14 o posterior" | El PostgreSQL de mayor versión del equipo es anterior a 14 (1) | El mensaje dice la versión y la carpeta | Actualizar PostgreSQL |
| "No se puede instalar una versión anterior" | La versión instalada es mayor (7) | El mensaje dice ambas versiones | Usar el instalador de esa versión o una posterior; `/ALLOWDOWNGRADE` sólo si se sabe que las migraciones son compatibles |
| Instalación silenciosa termina sin instalar | Necesita la contraseña de `postgres` (7) | Mensaje de `PrepareToInstall` | Ejecutar con asistente |
| Pantalla final: "La base de datos NO quedó configurada" | Falló `psql` (3) | `Logs\db_setup.log` | Corregir lo que indique y volver a ejecutar el instalador: repite todo lo que falte |
| El sitio responde *HTTP Error 500.30* o no abre | Falta el Hosting Bundle o el sitio no arranca | Pantalla final; `Web\logs\bootstrap\bootstrap-*.txt` | Reinstalar (repara el Hosting Bundle); si la bitácora dice `Serilog:WriteTo:3:Name`, ver la sección 13, punto 1 |
| El sitio quedó en el puerto 8081 | El 80 estaba ocupado (5.1) | `Logs\iis_setup.log`: "Puerto 80 ocupado" | Liberar el 80 y reinstalar, o usar el 8081 |
| Otro sitio de IIS dejó de responder | El instalador detuvo `Default Web Site` (5.1) | Administrador de IIS | Volver a iniciarlo si se necesita y no usa el puerto 80 |
| Un enlace HTTPS o un cambio hecho en IIS desapareció tras actualizar | El instalador borra y recrea el sitio (5.1) | Administrador de IIS | Volver a aplicarlo después de cada actualización |
| Se cambió el puerto ADMS y los relojes dejaron de llegar | Ni IIS ni el firewall se actualizan (11.4) | `netsh advfirewall firewall show rule name="AttendanceSystem ADMS"` | Agregar el enlace en IIS y ajustar la regla, o volver a 18373 |
| El programa de escritorio dice que no encuentra `appsettings.json` | Falta el archivo (4) | El mensaje muestra la carpeta | Volver a ejecutar el instalador o crearlo a partir del ejemplo |
| Las tareas automáticas no corren con sólo el programa de escritorio | Sin sitio, sólo corren con el programa abierto | Aviso de la pantalla final | Marcar "Abrir el programa al iniciar sesión" o instalar el sitio |
| Los respaldos fallan por no encontrar `pg_dump` | `Backup:PostgresBinPath` vacío o equivocado (10.1) | La clave en el `appsettings` de la parte | Poner la carpeta `bin` de PostgreSQL |
| No llegan correos | Alertas apagadas o SMTP vacío (11.3) | Configuración del sistema | Configurar SMTP y activar alertas. Las claves `SendGrid` no sirven |
| Se perdió la contraseña de `admin` | — | `.initial_admin_credentials.txt` en la carpeta del programa, si se generó ([arquitectura, sección 9](01-arquitectura.md#9-arranque-y-modo-mantenimiento)) | Se trata en la fase de operación |

---

## 13. Comportamientos verificados que conviene revisar

Se confirmaron con pruebas o con la lectura del código; se tratarán en la fase de limitaciones.

1. **El sitio web instalado desde v2.3.0-beta.1 no arrancaba (resuelto en PR #33).** La plantilla del sitio fijaba
   `Serilog:WriteTo:3:Args:connectionString` ([appsettings.Web.Production.template.json](../../Deploy/appsettings.Web.Production.template.json))
   y suponía un `appsettings.json` con cuatro sinks, donde el cuarto era el de PostgreSQL. Ese archivo está en
   `.gitignore`, así que CI no lo tiene: el sitio publicado sólo trae el `appsettings.Production.json` del instalador.
   En v2.3.0-beta.1 Serilog lanzaba `The configuration value in Serilog:WriteTo:3:Name has no 'Name' element`; desde PR #33 la plantilla y el ejemplo configuran sin error
   (pruebas `I01`, `I02` e `I03`, [InstalacionDocumentacionTests.cs:28](../../tests/AttendanceSystem.Infrastructure.Tests/Documentation/InstalacionDocumentacionTests.cs#L28)).
   La configuración de Serilog se aplica al construir el host
   ([Program.cs:66-67](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L66-L67),
   [línea 162](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L162)), y el error terminaba el proceso
   ([líneas 305-309](../../src/Presentation/AttendanceSystem.Blazor.Server/Program.cs#L305-L309)). De las versiones
   publicadas sólo afectó a **v2.3.0-beta.1**.
2. **Un `appsettings.json` local se incluye en el instalador.** `dotnet publish` copia el `appsettings.json` del
   proyecto si existe, y el instalador sólo excluye `appsettings.Production.json` del sitio y del servicio
   ([setup_script.iss:127](../../Deploy/setup_script.iss#L127), [línea 133](../../Deploy/setup_script.iss#L133)). Un
   instalador compilado en el equipo de un desarrollador lleva su configuración local, con lo que contenga.
3. **Cada actualización borra y recrea el sitio de IIS** y detiene `Default Web Site` (sección 5.1).
4. **Cambiar el puerto ADMS en la configuración no ajusta IIS ni el firewall** (sección 11.4).
5. **Cinco campos de configuración no tienen efecto** y las claves `SendGrid:*` tampoco (secciones 11.6 y 10.1).
6. **La `ApiKey` del servicio ZKTeco también autoriza dos rutas del sitio** (`reload-jobs` y `external-logs`), porque
   el sitio la busca primero (sección 10.1).
7. **El puerto ADMS por omisión es 18373 en el dominio y 16373 en la pantalla** ([relojes, 9](04-dispositivos.md#9-comportamientos-verificados-que-conviene-revisar), punto 9).
8. **Desinstalar deja la base, las contraseñas y la configuración** en el equipo (sección 7).
