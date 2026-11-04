# Guía para Generar el Instalador "Todo en Uno"

Este documento explica cómo generar el instalador final (`setup.exe`) para el sistema de asistencia. Este instalador incluirá:
- La aplicación Web (Blazor Server) y/o el programa de escritorio (WPF), a elección de quien instala
- El servicio de Windows (ZKTeco Service)
- El motor de base de datos PostgreSQL (instalación silenciosa)
- Configuración automática de la base de datos

## Prerrequisitos

1.  **Inno Setup Compiler**: Descargar e instalar la última versión (6.x o superior) desde [jrsoftware.org](https://jrsoftware.org/isdl.php).
2.  **Descarga Automática de Prerrequisitos (Recomendada)**:
    Ejecuta el script incluido para descargar automáticamente PostgreSQL y el ASP.NET Core Hosting Bundle:
    ```powershell
    .\Deploy\Download-Prerequisites.ps1
    ```
    El Hosting Bundle se descarga de la misma versión mayor de .NET que el `TargetFramework` del proyecto web (hoy .NET 10). Si el `dotnet-hosting.exe` existente es de otra versión, se reemplaza.
3.  **Descarga Manual (Alternativa)**:
    - **PostgreSQL**: Descargar el instalador para Windows x64 desde [enterprisedb.com](https://www.enterprisedb.com/downloads/postgres-postgresql-downloads), renombrarlo a `postgresql-installer.exe` y colocarlo en `Deploy\`.
    - **ASP.NET Core Hosting Bundle**: Descargar desde Microsoft la versión de .NET de la app, renombrarlo a `dotnet-hosting.exe` y colocarlo en `Deploy\`.

> `setup_script.iss` **no compila** si `dotnet-hosting.exe` no es de la misma versión mayor de .NET que `Release\Web` (o si falta `Release\Web`). Así no se puede entregar un instalador con un Hosting Bundle que IIS no pueda usar.

## Pasos para Generar el Instalador

### 1. Preparar los Archivos de la Aplicación
Abre una terminal de PowerShell en la raíz del proyecto y ejecuta el script de preparación:

```powershell
.\Prepare-Release.ps1
```

Este script compilará la aplicación web, el programa de escritorio y el servicio en modo "Autocontenido" (no requiere instalar .NET aparte) y colocará los archivos en `Release\Web`, `Release\Desktop` y `Release\Service`. Se detiene si falla alguna publicación o si falta un archivo crítico.

### 2. Compilar el Script de Instalación
1.  Abre la carpeta `Deploy`.
2.  Haz doble clic en el archivo `setup_script.iss` (se abrirá con Inno Setup).
3.  Presiona **F9** o haz clic en **Build > Compile**.

### 3. Resultado Final
Una vez termine la compilación, encontrarás el archivo `AttendanceSystem_Setup.exe` en la carpeta `Output` (en la raíz del proyecto).

Este es el archivo único que debes entregar al usuario final.

## Características Incluidas

### 0. Sistemas soportados
Windows 10 (1809 o posterior) y Windows 11, y Windows Server 2019, 2022 y 2025 con escritorio, siempre de 64 bits. El instalador no se ejecuta en Windows de 32 bits ni en versiones anteriores (`MinVersion=10.0.17763`, `ArchitecturesAllowed=x64compatible`).

### 0.1 Sitio web, programa de escritorio o ambos
El asistente ofrece **Sitio web y programa de escritorio**, **Sólo sitio web (IIS)**, **Sólo programa de escritorio** o una selección personalizada. Siempre se instalan PostgreSQL (si no hay uno), la base de datos y el servicio ZKTeco, porque los usan las dos partes. Ambas parten de la misma base de datos y del mismo usuario, y el instalador configura cada una:

| | Sólo sitio web | Sólo programa de escritorio | Ambos |
|---|---|---|---|
| Configuración | `Web\appsettings.Production.json` | `Desktop\appsettings.json` con `Hosting:Standalone = true` | Las dos, con `Hosting:Standalone = false` |
| Migraciones, tareas programadas, relojes ADMS | El sitio | El programa, mientras está abierto | El sitio; el programa comparte las tareas |
| IIS y Hosting Bundle | Sí | No | Sí |
| Puerto ADMS (18373) | Enlace del sitio en IIS + firewall | Lo abre el programa + firewall | Enlace del sitio en IIS + firewall |

- El programa de escritorio queda en el menú Inicio. Opcionalmente, el instalador crea un acceso directo en el escritorio y uno para abrirlo al iniciar sesión (recomendado sin sitio web).
- Corre con la cuenta de quien lo abre: el instalador da permiso de modificación a *Usuarios* sobre sus carpetas `logs` y `Backups` (y sobre `Web\Backups` cuando conviven). Su `appsettings.json` contiene la cadena de conexión y es legible por los usuarios del equipo.
- **Agregar una parte después:** vuelva a ejecutar el instalador y márquela. La nueva parte se configura con la cadena de conexión y la ApiKey existentes, sin pedir la contraseña de `postgres`. Si se agrega el sitio a un equipo que sólo tenía el programa, el instalador cambia su `Hosting:Standalone` a `false`.
- **Quitar una parte:** el asistente no deja desmarcar algo ya instalado (Inno no borra sus archivos). Hay que desinstalar y volver a instalar sólo lo necesario.
- Una instalación anterior sin componentes (sólo sitio web) se actualiza como "Sólo sitio web".
- El programa de escritorio requiere la versión con `Hosting:Standalone` (PR #22, `feat/wpf-standalone`).

### 1. SDK ZKTeco
El instalador incluye las librerías DLL de 32 bits del SDK de ZKTeco (zkemkeeper.dll, plcommpro.dll, etc.). Las deja en `{app}\Service`, junto al servicio (x86), y registra `zkemkeeper.dll` desde ahí con el `regsvr32` de 32 bits. No copia nada a `System32` ni a `SysWOW64`:
- `regsvr32` y COM buscan las dependencias de un DLL registrado primero en su propia carpeta, así que se usan siempre las del SDK incluido.
- Otros programas de ZKTeco dejan en `SysWOW64` DLLs con el mismo nombre pero de otro SDK (por ejemplo `plcommpro.dll` 6.0.0.5 del PullSDK). Con ellos `zkemkeeper.dll` no carga y `regsvr32` sale con código 3; así, además, esos programas no se ven afectados.
- La desinstalación quita el registro.

No requieren el Visual C++ Redistributable (sólo dependen de DLLs de Windows).

### 2. Hospedaje en IIS (si se instala el sitio web)
La aplicación web se hospeda siempre en IIS. El instalador:
- Habilita sólo las características de IIS que necesita ASP.NET Core, incluido WebSockets (`Install-WindowsFeature` en Windows Server, `Enable-WindowsOptionalFeature` en Windows 10/11).
- Instala el Hosting Bundle si falta el módulo ANCM de la versión de la app, o lo repara si el módulo existe pero no está registrado en IIS.
- Crea el AppPool y el sitio `AttendanceSystem` en el puerto 80 (o en el 8081 si el 80 está ocupado), con un segundo enlace en el puerto ADMS 18373 para los relojes. Si se cambia el puerto ADMS en la configuración del sistema, hay que ajustar a mano ese enlace y la regla de firewall `AttendanceSystem ADMS`.

La web no se registra como servicio ni se ejecuta fuera de IIS.

### 3. PostgreSQL existente
Detecta cualquier PostgreSQL 14 o posterior, esté registrado o sólo en `C:\Program Files\PostgreSQL\<versión>`. Si hay varios, usa el de versión más alta y lee su puerto real de `postgresql.conf`. Comprueba la contraseña de `postgres` en el mismo asistente antes de instalar. La cadena de conexión y `Backup:PostgresBinPath` (para `pg_dump` / `pg_restore`) apuntan a esa instalación.

### 4. Verificación final
Al terminar, el instalador comprueba que IIS esté habilitado, que el módulo ASP.NET Core esté registrado, que exista el sitio y que el servicio ZKTeco esté en ejecución. Cualquier problema se muestra junto con su log en `{app}\Logs` (`db_setup.log`, `enable_iis.log`, `dotnet_hosting.log`, `iis_setup.log`).

## Notas Técnicas
- **Primera instalación vs. actualización**: el instalador considera "primera instalación" cuando no existen ni `{app}\Web\appsettings.Production.json` ni `{app}\Desktop\appsettings.json`. Sólo entonces:
    - Genera secretos aleatorios (contraseña del usuario de BD, ApiKey de gRPC y, si instala PostgreSQL, la del superusuario `postgres`).
    - Ejecuta `init_db.sql` como `postgres`: crea/actualiza el usuario `attendancesystem_user`, la base `AttendanceSystem` (de su propiedad) y los esquemas `public` y `hangfire`.
- **Configuración de cada parte**: en cada ejecución se escribe la que falte, a partir de las plantillas `appsettings.Web.Production.template.json`, `appsettings.Desktop.template.json` y `appsettings.Service.Production.template.json`, todas con la misma cadena de conexión y ApiKey.
- **Actualizaciones**: las configuraciones existentes **no se tocan** (salvo `Hosting:Standalone` del programa, que sigue a si el sitio está instalado); ahí deben ir los cambios propios de cada equipo. No se pide la contraseña de `postgres` ni se reconfigura la BD. `appsettings.json` del sitio y del servicio sí se reemplaza en cada versión.
- **Contraseña de `postgres`**: si el instalador instaló PostgreSQL, la guarda en `{app}\Database\postgres_superuser.txt` (sólo Administradores y SYSTEM). Si PostgreSQL ya existía, se pide en el asistente y sólo se usa durante la instalación (no se guarda).
- **Errores de BD**: si no se puede configurar la base de datos, el instalador lo avisa al final y deja el detalle en `{app}\Logs\db_setup.log`; los `appsettings.Production.json` no se escriben, así que al volver a ejecutar el instalador se reintenta todo.
- **Puertos y Red**: el instalador abre el puerto ADMS 18373 siempre, y los puertos 80 y 8081 sólo con el sitio web, en el Firewall de Windows. El servicio gRPC del puente de dispositivos (puerto 5001) permanece restringido a loopback (`127.0.0.1` / `localhost`) por defecto para máxima seguridad.
- **Servicios**: se instalan dos servicios de Windows:
    - `postgresql-x64-<versión>`: motor de base de datos (el existente, o el incluido en `postgresql-installer.exe`, hoy la versión 15).
    - `AttendanceSystem.ZKTeco.Service`: servicio de comunicación con dispositivos.
