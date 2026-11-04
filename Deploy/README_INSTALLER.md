# Guía para Generar el Instalador "Todo en Uno"

Este documento explica cómo generar el instalador final (`setup.exe`) para el sistema de asistencia. Este instalador incluirá:
- La aplicación Web (Blazor Server)
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

Este script compilará la aplicación web y el servicio en modo "Autocontenido" (no requiere instalar .NET aparte) y colocará los archivos en la carpeta `Release`.

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

### 1. SDK ZKTeco
El instalador incluye las librerías DLL de 32 bits del SDK de ZKTeco (zkemkeeper.dll, plcommpro.dll, etc.). Las copia a `SysWOW64` y registra `zkemkeeper.dll` con el `regsvr32` de 32 bits, que es donde las busca el servicio (x86). No reemplaza una versión más nueva del SDK que haya instalado otro programa, y al desinstalar sólo las borra si nadie más las usa. No requieren el Visual C++ Redistributable (sólo dependen de DLLs de Windows).

### 2. Hospedaje en IIS (obligatorio)
La aplicación web se hospeda siempre en IIS. El instalador:
- Habilita sólo las características de IIS que necesita ASP.NET Core, incluido WebSockets (`Install-WindowsFeature` en Windows Server, `Enable-WindowsOptionalFeature` en Windows 10/11).
- Instala el Hosting Bundle si falta el módulo ANCM de la versión de la app, o lo repara si el módulo existe pero no está registrado en IIS.
- Crea el AppPool y el sitio `AttendanceSystem` en el puerto 80 (o en el 8081 si el 80 está ocupado).

No existe un modo independiente (Kestrel): la web no se registra como servicio ni tiene acceso directo.

### 3. PostgreSQL existente
Detecta cualquier PostgreSQL 14 o posterior, esté registrado o sólo en `C:\Program Files\PostgreSQL\<versión>`. Si hay varios, usa el de versión más alta y lee su puerto real de `postgresql.conf`. Comprueba la contraseña de `postgres` en el mismo asistente antes de instalar. La cadena de conexión y `Backup:PostgresBinPath` (para `pg_dump` / `pg_restore`) apuntan a esa instalación.

### 4. Verificación final
Al terminar, el instalador comprueba que IIS esté habilitado, que el módulo ASP.NET Core esté registrado, que exista el sitio y que el servicio ZKTeco esté en ejecución. Cualquier problema se muestra junto con su log en `{app}\Logs` (`db_setup.log`, `enable_iis.log`, `dotnet_hosting.log`, `iis_setup.log`).

## Notas Técnicas
- **Primera instalación vs. actualización**: el instalador considera "primera instalación" cuando no existe `{app}\Web\appsettings.Production.json`. Sólo entonces:
    - Genera secretos aleatorios (contraseña del usuario de BD, ApiKey de gRPC y, si instala PostgreSQL, la del superusuario `postgres`).
    - Ejecuta `init_db.sql` como `postgres`: crea/actualiza el usuario `attendancesystem_user`, la base `AttendanceSystem` (de su propiedad) y los esquemas `public` y `hangfire`.
    - Escribe `Web\appsettings.Production.json` y `Service\appsettings.Production.json` (plantillas `appsettings.*.Production.template.json`) con esa cadena de conexión y la misma ApiKey.
- **Actualizaciones**: los `appsettings.Production.json` existentes **no se tocan**; ahí deben ir los cambios propios de cada equipo. No se pide la contraseña de `postgres` ni se reconfigura la BD. `appsettings.json` sí se reemplaza en cada versión.
- **Contraseña de `postgres`**: si el instalador instaló PostgreSQL, la guarda en `{app}\Database\postgres_superuser.txt` (sólo Administradores y SYSTEM). Si PostgreSQL ya existía, se pide en el asistente y sólo se usa durante la instalación (no se guarda).
- **Errores de BD**: si no se puede configurar la base de datos, el instalador lo avisa al final y deja el detalle en `{app}\Logs\db_setup.log`; los `appsettings.Production.json` no se escriben, así que al volver a ejecutar el instalador se reintenta todo.
- **Puertos y Red**: el instalador abre los puertos 80 y 8081 (Web) en el Firewall de Windows. El servicio gRPC del puente de dispositivos (puerto 5001) permanece restringido a loopback (`127.0.0.1` / `localhost`) por defecto para máxima seguridad.
- **Servicios**: se instalan dos servicios de Windows:
    - `postgresql-x64-<versión>`: motor de base de datos (el existente, o el incluido en `postgresql-installer.exe`, hoy la versión 15).
    - `AttendanceSystem.ZKTeco.Service`: servicio de comunicación con dispositivos.
