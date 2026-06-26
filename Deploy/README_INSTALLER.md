# Guía para Generar el Instalador "Todo en Uno"

Este documento explica cómo generar el instalador final (`setup.exe`) para el sistema de asistencia. Este instalador incluirá:
- La aplicación Web (Blazor Server)
- El servicio de Windows (ZKTeco Service)
- El motor de base de datos PostgreSQL (instalación silenciosa)
- Configuración automática de la base de datos

## Prerrequisitos

1.  **Inno Setup Compiler**: Descargar e instalar la última versión (6.x o superior) desde [jrsoftware.org](https://jrsoftware.org/isdl.php).
2.  **Instalador de PostgreSQL**:
    - Descargar la versión 16.x para Windows x64 desde [enterprisedb.com](https://www.enterprisedb.com/downloads/postgres-postgresql-downloads).
    - **IMPORTANTE**: Renombra el archivo a `postgresql-installer.exe` y colócalo en la carpeta `Deploy`.
3.  **ASP.NET Core Hosting Bundle 9.0**:
    - Descargar desde el sitio oficial de Microsoft (.NET 9.0 Hosting Bundle).
    - **IMPORTANTE**: Renombra el archivo a `dotnet-hosting.exe` y colócalo en la carpeta `Deploy`.

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

### 1. SDK ZKTeco
El instalador incluye automáticamente todas las librerías DLL necesarias para la comunicación con dispositivos ZKTeco (zkemkeeper.dll, plcommpro.dll, etc.) y las registra en el sistema. No es necesario instalar ningun SDK adicional por separado.

### 2. Soporte para IIS (Opcional)
El instalador ofrece una opción llamada **"Configurar como servidor IIS"**.

- **Si NO se marca (Recomendado):** La aplicación web funcionará de forma independiente (Self-Hosted) en el puerto 8081. Se creará un acceso directo en el escritorio para iniciarla.
- **Si SE marca:** El instalador intentará configurar un Sitio Web en el IIS local (puerto 80) y un AppPool dedicado.
    - **Requisito Previo:** Para que esta opción funcione, el servidor debe tener instalado el **ASP.NET Core Hosting Bundle** y el rol de IIS activado.
    - Si IIS no está instalado, esta opción simplemente se omitirá y la app quedará instalada pero sin configurar en IIS.

## Notas Técnicas
- **Usuario de Base de Datos**: El instalador crea un usuario PostgreSQL llamado `attendancesystem_user` con contraseña `Blanquita.123`.
- **Puertos**: El instalador abre automáticamente los puertos 8081 (Web) y 5001 (Servicio) en el Firewall de Windows.
- **Servicios**: Se instalan dos servicios de Windows:
    - `postgresql-x64-16`: Motor de base de datos
    - `AttendanceSystem.ZKTeco`: Servicio de comunicación con dispositivos
- **Ejecución de la Web**: La aplicación web NO se instala como servicio de Windows por defecto en este script, sino como una aplicación independiente. Para ejecutarla, el usuario debe usar el acceso directo "AttendanceSystem" en el escritorio o menú inicio. Si deseas que la web también sea un servicio, se requiere una configuración adicional en `setup_script.iss`.
