# 🚀 Guía de Despliegue en Producción - Attendance System

Esta guía proporciona los procedimientos paso a paso para desplegar **Attendance System** en servidores de producción Windows (Windows Server 2019/2022/2025 o Windows 10/11 Pro).

---

## 📋 Métodos de Despliegue Disponibles

Existen dos alternativas para desplegar el sistema:

1. **Método 1 (Recomendado): Instalador Todo-en-Uno (`AttendanceSystem_Setup.exe`)**
   - Automatiza la instalación de PostgreSQL, .NET Hosting Bundle, registro de DLLs de ZKTeco, creación de servicios y reglas de Firewall.
2. **Método 2: Despliegue Manual (Self-Hosted o IIS)**
   - Para administradores de TI que requieren configurar bases de datos existentes o arquitecturas personalizadas.

---

## 🛠️ Requisitos Previos

Antes de comenzar la instalación en el servidor, asegúrese de contar con:

- [x] **Sistema Operativo**: Windows 10/11 Pro (64-bit) o Windows Server 2019/2022/2025 Standard (64-bit).
- [x] **Permisos**: Cuenta de usuario con privilegios de **Administrador**.
- [x] **Hardware Mínimo**: CPU x64 (2 núcleos), 4-8 GB RAM, 20 GB de almacenamiento SSD.
- [x] **Conectividad**: Acceso a la red local (LAN o VPN) donde se ubican los relojes checadores (puertos 4370 TCP/UDP y 80/443 TCP).

---

## 📦 Método 1: Despliegue con Instalador Todo-en-Uno

### Paso 1: Generar los Artefactos de Release
En la máquina de desarrollo / compilación:
```powershell
# 1. Preparar archivos autocontenidos
.\Prepare-Release.ps1

# 2. Descargar prerrequisitos si no están presentes
.\Deploy\Download-Prerequisites.ps1
```

### Paso 2: Compilar el Instalador
1. Abra `Deploy\setup_script.iss` en **Inno Setup Compiler**.
2. Presione **F9** para compilar.
3. El archivo `Output\AttendanceSystem_Setup.exe` estará listo para distribución.

### Paso 3: Ejecutar la Instalación en el Servidor
1. Copie `AttendanceSystem_Setup.exe` al servidor.
2. Haga clic derecho $\rightarrow$ **Ejecutar como Administrador**.
3. Seleccione el modo de despliegue:
   - **Self-Hosted Kestrel (Predeterminado):** La aplicación web se ejecuta en el puerto `8081`.
   - **Servidor IIS:** Configure la aplicación en el puerto `80` bajo Internet Information Services.
4. Si PostgreSQL ya está instalado, ingrese la contraseña del superusuario `postgres`; de lo contrario, el instalador desplegará una instancia silenciosa.
5. Al finalizar, el instalador iniciará el servicio ZKTeco y dejará el sistema listo.

---

## ⚙️ Método 2: Despliegue Manual

### Paso 1: Configurar PostgreSQL
1. Instale PostgreSQL 16 (x64) en el servidor.
2. Abra `psql` o pgAdmin y ejecute:
```sql
-- Crear usuario de aplicación
CREATE USER attendancesystem_user WITH PASSWORD 'Blanquita.123';

-- Crear base de datos
CREATE DATABASE "AttendanceSystem" WITH OWNER = attendancesystem_user;

-- Otorgar permisos
GRANT ALL PRIVILEGES ON DATABASE "AttendanceSystem" TO attendancesystem_user;
```

### Paso 2: Publicar y Desplegar la Aplicación Web (Blazor)
1. Publique el proyecto en modo autocontenido x64:
   ```powershell
   dotnet publish src/Presentation/AttendanceSystem.Blazor.Server -c Release -r win-x64 --self-contained true -o "C:\Program Files\AttendanceSystem\Web"
   ```
2. Configure el archivo `appsettings.Production.json` en `C:\Program Files\AttendanceSystem\Web\`:
   ```json
   {
     "ConnectionStrings": {
       "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=attendancesystem_user;Password=Blanquita.123;",
       "HangfireDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=attendancesystem_user;Password=Blanquita.123;"
     },
     "ZKTecoService": {
       "Url": "http://localhost:5001"
     }
   }
   ```

### Paso 3: Publicar e Instalar el Servicio Windows ZKTeco (x86)
1. Publique el servicio en modo autocontenido x86:
   ```powershell
   dotnet publish src/Presentation/AttendanceSystem.ZKTeco.Service -c Release -r win-x86 --self-contained true -o "C:\Program Files\AttendanceSystem\Service" /p:PublishSingleFile=false
   ```
2. Copie las DLLs del SDK ZKTeco (`src/Infrastructure/AttendanceSystem.ZKTeco/lib/*.dll`) a `C:\Program Files\AttendanceSystem\Service\` y a `C:\Windows\SysWOW64\`.
3. Registre la librería COM en una consola de Administrador:
   ```cmd
   regsvr32.exe /s "C:\Windows\SysWOW64\zkemkeeper.dll"
   ```
4. Cree e inicie el servicio de Windows:
   ```cmd
   sc.exe create AttendanceSystem.ZKTeco.Service binPath= "C:\Program Files\AttendanceSystem\Service\AttendanceSystem.ZKTeco.Service.exe" start= auto displayname= "Attendance System ZKTeco Service"
   sc.exe description AttendanceSystem.ZKTeco.Service "Servicio puente gRPC x86 para relojes biometricos ZKTeco"
   sc.exe start AttendanceSystem.ZKTeco.Service
   ```

### Paso 4: Configuración de Firewall
Abra los puertos requeridos en Windows Firewall:
```powershell
# Puerto Web
New-NetFirewallRule -DisplayName "AttendanceSystem Web (8081)" -Direction Inbound -LocalPort 8081 -Protocol TCP -Action Allow

# Puerto gRPC (local)
New-NetFirewallRule -DisplayName "AttendanceSystem gRPC (5001)" -Direction Inbound -LocalPort 5001 -Protocol TCP -Action Allow
```

---

## 🔍 Verificación Post-Despliegue

Ejecute el script de diagnóstico incluido para comprobar la salud de todos los componentes:

```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
.\Docs\Diagnose-AttendanceSystem.ps1
```

### Comprobaciones esperadas:
* [x] Servicio Windows `AttendanceSystem.ZKTeco.Service` en estado `Running`.
* [x] Puerto gRPC `5001` en estado `Listen`.
* [x] Conexión TCP a PostgreSQL `5432` exitosa.
* [x] Acceso web a `http://localhost:8081` (o `http://localhost` en IIS).

---

## 🔑 Credenciales Iniciales

| Sistema | Usuario | Contraseña |
| :--- | :--- | :--- |
| **Aplicación Web** | `admin` | `Admin123!` |
| **PostgreSQL (App User)** | `attendancesystem_user` | `Blanquita.123` |

> [!WARNING]
> Cambie la contraseña del usuario `admin` inmediatamente después del primer inicio de sesión desde el menú de usuario.

---

## 📞 Solución de Problemas Comunes

1. **Error: `RpcException: Unavailable` al probar dispositivos ZKTeco**:
   - El servicio Windows ZKTeco no está corriendo o el puerto 5001 está bloqueado.
   - Ejecute `.\Docs\Reinstall-ZKTecoService.ps1` para reiniciar y verificar el servicio.
2. **Error al cargar plantillas biométricas o DLL no encontrada**:
   - Falta registrar `zkemkeeper.dll` con `regsvr32` en `SysWOW64` o falta el paquete *Visual C++ 2015-2022 Redistributable (x86)*.
3. **Error en Blazor Web (SignalR / WebSockets desconectado)**:
   - Si se aloja en IIS, verifique que la característica de servidor `WebSockets` esté instalada.
