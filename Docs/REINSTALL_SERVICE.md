# 🔄 Guía de Reinstalación del Servicio Windows ZKTeco

Esta guía describe el procedimiento para detener, desinstalar y reinstalar limpiamente el servicio Windows **AttendanceSystem.ZKTeco.Service** encargado de la comunicación con los relojes checadores ZKTeco (arquitectura x86).

---

## 🎯 ¿Cuándo es necesario reinstalar el servicio?

- ✅ Se actualizaron los binarios o librerías DLL del SDK ZKTeco.
- ✅ Se modificó el archivo de configuración `appsettings.json` o variables de entorno del servicio.
- ✅ El servicio no inicia, muestra estado *Paused* o arroja errores de carga de librerías COM (`zkemkeeper.dll`).
- ✅ Se cambió la ruta de instalación en el servidor.
- ✅ La aplicación Blazor reporta `RpcException: Unavailable (StatusCode=Unavailable)` de forma persistente.

---

## ⚡ Método 1: Reinstalación Automatizada con PowerShell (Recomendado)

Se incluye un script automatizado que realiza todos los pasos de manera segura:

### Instrucciones:
1. Abra **PowerShell como Administrador** en el servidor.
2. Navegue al directorio del proyecto o de instalación:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
   .\Docs\Reinstall-ZKTecoService.ps1
   ```
3. El script se encargará automáticamente de:
   - Detener el servicio si está corriendo.
   - Eliminar el registro previo en el Service Control Manager (`sc.exe delete`).
   - Copiar y registrar las DLLs COM de 32 bits (`zkemkeeper.dll`) en `C:\Windows\SysWOW64`.
   - Recrear el servicio con inicio automático (`start= auto`).
   - Iniciar el servicio y comprobar la escucha en el puerto `5001`.

---

## 🛠️ Método 2: Reinstalación Manual (Paso a Paso)

Si prefiere realizar el procedimiento manualmente desde la consola de comandos de Windows (CMD como Administrador):

### 1. Detener el Servicio Existente
```cmd
net stop AttendanceSystem.ZKTeco.Service
```
*(o mediante `sc.exe stop AttendanceSystem.ZKTeco.Service`)*

### 2. Eliminar el Servicio
```cmd
sc.exe delete AttendanceSystem.ZKTeco.Service
```

### 3. Re-registrar las Librerías COM de ZKTeco (32-bit)
Copie las DLLs de la carpeta del servicio a `SysWOW64` y registre la librería principal:
```cmd
copy "C:\Program Files\AttendanceSystem\Service\*.dll" "C:\Windows\SysWOW64\" /Y
regsvr32.exe /s "C:\Windows\SysWOW64\zkemkeeper.dll"
```

### 4. Crear el Nuevo Servicio de Windows
```cmd
sc.exe create AttendanceSystem.ZKTeco.Service binPath= "C:\Program Files\AttendanceSystem\Service\AttendanceSystem.ZKTeco.Service.exe" start= auto displayname= "Attendance System ZKTeco Service"
sc.exe description AttendanceSystem.ZKTeco.Service "Servicio puente gRPC x86 para comunicacion con relojes biometricos ZKTeco"
```

### 5. Iniciar el Servicio
```cmd
net start AttendanceSystem.ZKTeco.Service
```

---

## 🔍 Verificación Post-Reinstalación

1. **Comprobar Estado del Servicio:**
   ```powershell
   Get-Service "AttendanceSystem.ZKTeco.Service"
   ```
   *El estado debe ser `Running`.*

2. **Comprobar Puerto gRPC (5001):**
   ```powershell
   Get-NetTCPConnection -LocalPort 5001 -State Listen
   ```

3. **Ejecutar Diagnóstico Completo:**
   ```powershell
   .\Docs\Diagnose-AttendanceSystem.ps1
   ```

4. **Revisar Visor de Eventos (Event Viewer):**
   Si el servicio no arranca, abra `eventvwr.msc` $\rightarrow$ *Registros de Windows* $\rightarrow$ *Aplicación* y filtre por origen `AttendanceSystem.ZKTeco.Service`.
