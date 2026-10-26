# 📗 Manual de Operaciones - Attendance System

Bienvenido al **Manual de Operaciones** oficial de **Attendance System**. Esta guía está diseñada para proporcionar a los administradores de recursos humanos, supervisores de área, operadores de nómina y personal de TI las instrucciones paso a paso para utilizar cada una de las funcionalidades de la aplicación web.

---

## 📑 Tabla de Contenidos

1. [Introducción y Arquitectura Funcional](#1-introducción-y-arquitectura-funcional)
2. [Acceso al Sistema y Seguridad](#2-acceso-al-sistema-y-seguridad)
3. [Navegación y Dashboard Principal](#3-navegación-y-dashboard-principal)
4. [Gestión de Personal](#4-gestión-de-personal)
   * 4.1 [Empleados (Alta, Edición y Bajas)](#41-empleados-alta-edición-y-bajas)
   * 4.2 [Enrolamiento y Parámetros Biométricos](#42-enrolamiento-y-parámetros-biométricos)
   * 4.3 [Importación y Exportación de Empleados](#43-importación-y-exportación-de-empleados)
   * 4.4 [Sucursales](#44-sucursales)
   * 4.5 [Departamentos](#45-departamentos)
   * 4.6 [Puestos de Trabajo](#46-puestos-de-trabajo)
   * 4.7 [Horarios y Turnos](#47-horarios-y-turnos)
5. [Dispositivos y Descargas](#5-dispositivos-y-descargas)
   * 5.1 [Administración de Relojes Checadores](#51-administración-de-relojes-checadores)
   * 5.2 [Métodos de Conexión: SDK (Pull) vs ADMS (Push)](#52-métodos-de-conexión-sdk-pull-vs-adms-push)
   * 5.3 [Acciones de Diagnóstico y Control de Dispositivos](#53-acciones-de-diagnóstico-y-control-de-dispositivos)
   * 5.4 [Descargas e Historial de Sincronización](#54-descargas-e-historial-de-sincronización)
   * 5.5 [Registros Manuales de Asistencia](#55-registros-manuales-de-asistencia)
   * 5.6 [Cálculo y Reprocesamiento de Asistencia](#56-cálculo-y-reprocesamiento-de-asistencia)
6. [Reportes y Análisis](#6-reportes-y-análisis)
   * 6.1 [Reporte de Asistencia (Kárdex General)](#61-reporte-de-asistencia-kárdex-general)
   * 6.2 [Análisis de Ausentismo](#62-análisis-de-ausentismo)
   * 6.3 [Análisis de Retardos](#63-análisis-de-retardos)
   * 6.4 [Impresión de Checadores (Tarjetas de Asistencia)](#64-impresión-de-checadores-tarjetas-de-asistencia)
   * 6.5 [Reportes Avanzados](#65-reportes-avanzados)
   * 6.6 [Logs de Asistencia en Bruto](#66-logs-de-asistencia-en-bruto)
7. [Configuración y Mantenimiento del Sistema](#7-configuración-y-mantenimiento-del-sistema)
   * 7.1 [Configuración General y Parámetros Globales](#71-configuración-general-y-parámetros-globales)
   * 7.2 [Respaldo y Restauración de Base de Datos](#72-respaldo-y-restauración-de-base-de-datos)
   * 7.3 [Gestión de Usuarios y Roles](#73-gestión-de-usuarios-y-roles)
   * 7.4 [Monitoreo de Tareas Programadas (Hangfire)](#74-monitoreo-de-tareas-programadas-hangfire)
8. [Resolución de Problemas Frecuentes](#8-resolución-de-problemas-frecuentes)
9. [Preguntas Frecuentes (FAQ)](#9-preguntas-frecuentes-faq)

---

## 1. Introducción y Arquitectura Funcional

**Attendance System** es una plataforma integral de control de asistencia desarrollada sobre .NET 10 y Blazor Server. Automatiza la recolección de eventos biométricos (huella digital, reconocimiento facial, tarjeta de proximidad RFID y contraseña) desde dispositivos compatibles (ZKTeco y Hikvision), calculando las incidencias laborales bajo las políticas de la organización para alimentar la pre-nómina.

### Roles de Usuario y Perfiles de Acceso

El sistema implementa control de acceso basado en roles (RBAC) con **ASP.NET Core Identity**:

| Rol | Alcance y Permisos |
| :--- | :--- |
| **Administrador** | <ul><li>Control total de todos los módulos del sistema.</li><li>Gestión y diagnóstico de hardware (relojes checadores).</li><li>Administración de sucursales, configuración global y usuarios.</li><li>Registros manuales de checadas y reprocesamiento de cálculo.</li><li>Respaldos de base de datos y monitoreo de Hangfire.</li></ul> |
| **Supervisor** | <ul><li>Gestión y consulta del personal a su cargo.</li><li>Revisión de departamentos, puestos, empleados y horarios.</li><li>Generación y exportación de reportes de asistencia y kárdex.</li><li>Impresión de tarjetas de control de checadores.</li></ul> |
| **Usuario** | <ul><li>Consulta de reportes de asistencia y visualización de métricas generales en modo lectura.</li></ul> |

---

## 2. Acceso al Sistema y Seguridad

### 2.1 Inicio de Sesión
1. Abra su navegador web (Google Chrome, Microsoft Edge o Mozilla Firefox).
2. Ingrese a la dirección web del sistema:
   * Despliegue estándar: `http://localhost:8081` o la dirección IP asignada en su red corporativa (ej. `http://192.168.1.242:8081`).
   * Despliegue en IIS: `http://servidor-asistencia` o `https://asistencia.suempresa.com`.
3. Introduzca sus credenciales:
   * **Usuario:** `admin` (o su nombre de usuario asignado).
   * **Contraseña:** Su contraseña de acceso.
4. Haga clic en **INGRESAR**.

> [!TIP]
> Si olvida su contraseña o su cuenta es bloqueada por intentos fallidos, solicite a un usuario con rol de **Administrador** que restablezca su clave desde el módulo **Configuración $\rightarrow$ Usuarios**.

---

## 3. Navegación y Dashboard Principal

Al ingresar al sistema, accederá al **Panel de Control (Inicio)** (`/`), el cual presenta un panorama en tiempo real de la operación de asistencia de la empresa:

* **Métricas Principales (Tarjetas Superiores):**
  * **Total de Empleados:** Número total de trabajadores registrados en el sistema.
  * **Dispositivos Conectados:** Relojes checadores en estado *En Línea* frente al total registrado.
  * **Asistencias del Día:** Cantidad de colaboradores que han registrado su entrada hoy.
  * **Retardos del Día:** Empleados que registraron su entrada superando los minutos de tolerancia permitidos.
  * **Ausencias:** Empleados programados para laborar hoy que no han registrado checada.
* **Gráficos de Puntualidad y Estado:** Distribución porcentual entre Asistencias puntuales, Retardos y Faltas del día.
* **Accesos Rápidos:** Enlaces directos a *Descargas de Dispositivos*, *Cálculo de Asistencia* y *Reportes*.

El menú lateral izquierdo organiza los módulos en cuatro secciones colapsables:
1. **Gestión de personal**
2. **Dispositivos y descargas**
3. **Reportes y análisis**
4. **Configuración** *(Solo Administrador)*

---

## 4. Gestión de Personal

### 4.1 Empleados (Alta, Edición y Bajas)
Ubicación: **Gestión de personal $\rightarrow$ Empleados** (`/employees`)

La pantalla de empleados cuenta con un DataGrid interactivo con soporte de búsqueda global, ordenamiento por columnas y filtros avanzados por ID, Nombre, Sucursal, Departamento, Puesto y Estado.

#### Para dar de alta un nuevo empleado:
1. Haga clic en el botón **+ Nuevo Empleado** (esquina superior derecha).
2. Se abrirá el formulario de captura con 4 pestañas:

#### Pestaña 1: Datos Personales
* **ID / Número de Empleado:** Identificador único alfanumérico (ejemplo: `EMP001` o `1024`). Este ID debe coincidir con el ID configurado en el reloj checador.
* **Nombre(s)** y **Apellidos:** Nombre completo del trabajador.
* **Correo Electrónico** y **Teléfono:** Datos de contacto (opcionales).
* **Fecha de Contratación:** Fecha de ingreso a la empresa.
* **Género:** Masculino / Femenino / No especificado.
* **Estado:** `Alta`, `Vacaciones` o `Baja`. *(Los empleados en estado Baja no son contemplados en los cálculos diarios de asistencia).*

#### Pestaña 2: Organización
* **Sucursal:** Seleccione la sede o sucursal donde labora el empleado.
* **Departamento:** Área organizativa a la que pertenece.
* **Puesto:** Puesto de trabajo asignado.

#### Pestaña 3: Horario y Asistencia
* **Tipo de Horario:** Seleccione entre *Matutino*, *Vespertino*, *Nocturno* o *Abierto*.
* **Turno Asignado:** Seleccione el horario oficial que debe cumplir el colaborador (ej. *08:00 - 16:00*).
* **Día de Descanso Semanal:** Día de la semana asignado como descanso oficial (ej. *Domingo*).
* **Autorización de Horas Extras:** Active esta casilla si el trabajador tiene permitido devengar tiempo extra. Si la casilla está desactivada, el sistema ignorará cualquier excedente de tiempo laboral en los reportes de nómina.
* **Cálculo de Horas Extra Antes de la Entrada:** Habilite si se reconocen horas extras previas al inicio de la jornada.
* **Límite de Horas Extra:** Permite definir topes de tiempo extra (Diario / Semanal / Sin límite).

#### Pestaña 4: Biometría y Credenciales
* **Número de Tarjeta RFID:** Código numérico de la tarjeta de proximidad para el acceso.
* **Contraseña en Dispositivo:** Clave numérica opcional para checar en el teclado del dispositivo.
* **Privilegio en Dispositivo:** `Usuario normal` o `Administrador del reloj`.
* **Indicadores Biométricos:** Muestra si el empleado tiene registradas huellas digitales o plantilla facial.

3. Haga clic en **Guardar**.

---

### 4.2 Enrolamiento y Parámetros Biométricos

Para registrar las huellas digitales o el rostro de un colaborador:

#### Opcion 1
1. **Creación previa en el sistema:** Asegúrese de que el empleado esté registrado en el sistema con su **ID** correcto.
2. **Registro en el Reloj Checador Físico:**
   * En el dispositivo biométrico, acceda al menú presionando la tecla **M/OK**.
   * Ingrese a **Usuarios $\rightarrow$ Nuevo Usuario** o **Editar Usuario**.
   * Capture el mismo número de **ID** asignado en el sistema web.
   * Seleccione **Huella** y coloque el dedo sobre el sensor 3 veces consecutivas, o seleccione **Rostro** y mire a la cámara siguiendo las indicaciones en pantalla.
   * Guarde los cambios en el dispositivo.
3. **Descarga y Respaldo:**
   * En la aplicación web, vaya a **Dispositivos y descargas $\rightarrow$ Descargas e Historial** y ejecute una descarga de registros. Las plantillas quedarán vinculadas al ID del trabajador.

---

### 4.3 Importación y Exportación de Empleados

* **Importar Empleados:** Haga clic en **Importar** en la vista de empleados. Puede cargar un archivo Excel (`.xlsx`) respetando las columnas estándar:
  `Id`, `FirstName`, `LastName`, `Email`, `PhoneNumber`, `BranchCode`, `DepartmentName`, `PositionName`, `CardNumber`.
* **Exportar Lista:** Presione el botón **Exportar** para generar un archivo Excel (`.xlsx`) o PDF con todos los empleados filtrados en pantalla.

---

### 4.4 Sucursales
Ubicación: **Gestión de personal $\rightarrow$ Sucursales** (`/branches`) *(Solo Administrador)*

Permite gestionar las diferentes sedes físicas de la empresa:
* **Código de Sucursal:** Código corto de 3 caracteres (ejemplo: `A01`, `MTZ`, `SUC`).
* **Nombre:** Nombre identificador (ejemplo: *Planta Matriz*, *Sucursal Norte*).
* **Dirección:** Ubicación física de la sucursal.
* **Sucursal Externa:** Active esta opción si la sucursal es remota y se conecta vía API externa.

---

### 4.5 Departamentos
Ubicación: **Gestión de personal $\rightarrow$ Departamentos** (`/departments`)

Organiza las áreas funcionales de la empresa (ej. *Recursos Humanos*, *Sistemas*, *Producción*, *Ventas*):
* Permite asociar de forma múltiple qué puestos de trabajo pertenecen a cada departamento.

---

### 4.6 Puestos de Trabajo
Ubicación: **Gestión de personal $\rightarrow$ Puestos** (`/positions`)

Cataloga los cargos laborales disponibles en la organización con su nombre y descripción de responsabilidades.

---

### 4.7 Horarios y Turnos
Ubicación: **Gestión de personal $\rightarrow$ Horarios** (`/shifts`)

Los turnos definen las reglas de tiempo con las que se evalúa la puntualidad de los colaboradores:

* **Nombre del Turno:** Nombre descriptivo (ej. *Matutino 8h*, *Vespertino*, *Administrativo 08:00 a 16:00*).
* **Tipo de Turno:** `Matutino`, `Vespertino`, `Nocturno` o `Mixto`.
* **Hora de Entrada:** Hora programada de inicio de labores (ejemplo: `08:00:00`).
* **Hora de Salida:** Hora programada de final de labores (ej. `16:00:00`).
* **Horas de Trabajo (Jornada Objetivo):** Duración total en horas de la jornada regular (ejemplo: `8`).
* **Tolerancia en Entrada (Minutos):** Margen de gracia permitido antes de considerar retardo (ejemplo: `10` minutos).
  * *Regla de cálculo:* Si la entrada es a las `08:00` con `10` min de tolerancia, checar a las `08:10` es puntual. Si el empleado checa a las `08:11`, el sistema calcula **11 minutos de retardo** (el retardo se computa desde la hora oficial de entrada).

---

## 5. Dispositivos y Descargas

### 5.1 Administración de Relojes Checadores
Ubicación: **Dispositivos y descargas $\rightarrow$ Dispositivos** (`/devices`) *(Solo Administrador)*

Lista todos los relojes biométricos registrados mostrando su estado en tiempo real (**En línea** 🟢 / **Desconectado** 🔴), dirección IP, puerto, número de serie, modelo y método de comunicación.

#### Para agregar un nuevo dispositivo:
1. Haga clic en **+ Agregar Dispositivo**.
2. Capture los datos de configuración:
   * **ID del Dispositivo:** Identificador único (ej. `DEV-01`, `RELOJ-MATRIZ`).
   * **Nombre:** Nombre amigable (ej. *Entrada Principal*).
   * **Dirección IP:** IP estática asignada al reloj en la red LAN (ej. `192.168.1.201`).
   * **Puerto:** Por defecto `4370` para comunicación directa TCP/UDP.
   * **Número de Serie (SN):** Obligatorio para dispositivos que operen en modo ADMS (ej. `CKUH204160111`).
   * **Método de Descarga:** Seleccione `Sdk` (Pull) o `Adms` (Push).
   * **Tipo de Dispositivo:** `att` (Reloj checador de asistencia) o `acc` (Control de acceso / torniquetes).
   * **Sucursal Asignada:** Sede en la que se encuentra instalado el hardware.
3. Presione **Guardar**.

---

### 5.2 Métodos de Conexión: SDK (Pull) vs ADMS (Push)

* **Modo SDK (Pull / Conexión Directa):**
  * El servidor se conecta activamente a la IP del reloj a través del puerto `4370`.
  * Ideal para relojes dentro de la misma red local (LAN) o con VPN directa.
  * La comunicación se realiza mediante el servicio puente `AttendanceSystem.ZKTeco.Service`.
* **Modo ADMS (Push / Cloud Server):**
  * El reloj checador envía sus datos automáticamente al servidor web a través de peticiones HTTP en el puerto `8081` (rutas `/iclock/*`).
  * Ideal para sucursales remotas conectadas por Internet sin necesidad de abrir puertos ni tener IP pública fija en las sucursales.
  * En el reloj checador, configure:
    * **Dirección de Servidor de Nube:** Dirección IP o dominio del servidor de la aplicación (ej. `192.168.1.242`).
    * **Puerto de Servidor:** `8081` (o el puerto configurado en Kestrel/IIS).
    * **Habilitar Proxy de Dominio / Servidor Web:** Activado.

---

### 5.3 Acciones de Diagnóstico y Control de Dispositivos

Al hacer clic en el botón de opciones o en **Ver Detalles** de cualquier reloj checador, podrá ejecutar:

* 🕐 **Sincronizar Hora:** Ajusta de forma inmediata la fecha y hora interna del reloj checador para que coincida exactamente con la del servidor, configurando además la zona horaria correcta (`UTC-6`) y desactivando horario de verano para evitar desfases en las checadas.
* 🔄 **Probar Conexión:** Verifica el enlace de red y la respuesta de comunicación del reloj.
* 📋 **Consultar Opciones (Solo ADMS):** Envía únicamente `INFO` para consultar información técnica y opciones del dispositivo. No solicita descargas de checadas, usuarios ni biometría; los envíos automáticos configurados en el reloj son independientes de esta consulta.
* 🧹 **Borrar Registros:** Limpia la memoria interna de checadas del reloj (utilizar solo tras haber respaldado y descargado todas las asistencias).
* ⚙️ **Reinicio de Fábrica:** Restaura los parámetros de fábrica del hardware.
* 🔍 **Descubrimiento en Red:** Herramienta para escanear el segmento de red local y detectar automáticamente checadores ZKTeco conectados.

---

### 5.4 Descargas e Historial de Sincronización
Ubicación: **Dispositivos y descargas $\rightarrow$ Descargas e Historial** (`/attendance/download`)

Permite ejecutar descargas manuales de asistencias y consultar el historial completo de descargas realizadas por el sistema:

1. **Descarga Manual:**
   * Seleccione si desea descargar de un **Dispositivo Específico** o de **Todos los Dispositivos**.
   * Seleccione el modo de descarga:
     * **Sincronización Completa:** Descarga todo el histórico de marcaciones almacenado en la memoria del reloj.
     * **Rango de Fechas:** Descarga únicamente las checadas comprendidas en el intervalo seleccionado.
   * Haga clic en **Descargar Registros Ahora**.
2. **Historial de Descargas:**
   * Tabla con el registro de cada evento de descarga: fecha/hora, dispositivo, método, cantidad de registros nuevos procesados, duración y estado (*Exitoso* o *Error*).

---

### 5.5 Registros Manuales de Asistencia
Ubicación: **Dispositivos y descargas $\rightarrow$ Registros manuales** (`/attendance/manual-logs`) *(Solo Administrador)*

Permite capturar checadas olvidadas por colaboradores. Hay tres formas de hacerlo:

* **Individual** (desde **Bitácora de Asistencia**, botón **Agregar Manual**): seleccione el empleado, indique la **fecha y hora reales** de la checada y presione **Guardar**.
* **Asignación masiva**: seleccione el rango de fechas, la hora y los empleados. Se registra una checada por cada día y empleado.
* **Importar archivo** (.xlsx o .csv): columnas `EmployeeId`, `Date` y `Time`.

> [!NOTE]
> **El sistema decide el tipo de checada:**
> No se indica si la checada es entrada o salida. Al guardar, la asistencia se recalcula con las reglas normales tomando en cuenta la nueva checada, aunque el día ya estuviera calculado. Por ejemplo, si el empleado ya tenía entrada a las 09:00 y salida a las 17:00, una checada a las 18:30 pasa a ser la nueva salida. La única restricción es que no exista otra checada del mismo empleado en el mismo minuto.

> [!IMPORTANT]
> **Turnos que cruzan la medianoche:** capture siempre la fecha del calendario en que ocurrió la checada, no la fecha de inicio de la jornada. Por ejemplo, la salida de las 06:00 de un turno nocturno que inició el lunes a las 22:00 se captura como **martes 06:00**. El sistema la asigna a la jornada del lunes.

---

### 5.6 Cálculo y Reprocesamiento de Asistencia
Ubicación: **Dispositivos y descargas $\rightarrow$ Cálculo de asistencia** (`/attendance/calculation`)

El cálculo de asistencia procesa las checadas en bruto contra los turnos asignados para consolidar la tabla de asistencia diaria (`DailyAttendance`):

* **Cálculo por Día:** Seleccione una fecha específica y presione **Calcular Día**.
* **Cálculo por Rango de Fechas:** Seleccione fecha inicial, fecha final, filtre opcionalmente por sucursal o empleado y presione **Calcular Rango**.
* **Reglas aplicadas durante el cálculo:**
  * Identificación de primer checada como Entrada y última como Salida.
  * Cálculo de minutos de retardo si la entrada excede la tolerancia.
  * Identificación de salidas tempranas frente a la jornada programada.
  * Identificación de falta si transcurrió el día sin registro de entrada.
  * Detección de trabajo en día de descanso semanal (`WorkedOnRestDay`) y cómputo de horas extras para el personal autorizado sobre la jornada base de 8 horas.

---

## 6. Reportes y Análisis

### 6.1 Reporte de Asistencia (Kárdex General)
Ubicación: **Reportes y análisis $\rightarrow$ Reporte de Asistencia** (`/attendance/reports`)

Reporte principal para la revisión de la pre-nómina:

* **Filtros:** Rango de fechas (Desde / Hasta), Sucursal, Departamento y Selección de Empleado (o Todos).
* **Columnas del Reporte:**
  * Fecha, ID, Nombre Completo, Departamento, Turno programado.
  * Hora de Entrada programada vs Entrada Real.
  * Hora de Salida programada vs Salida Real.
  * Minutos de Retardo, Salida Anticipada y Horas Extras calculadas.
  * Estado de la jornada: `Asistencia`, `Retardo`, `Falta`, `Salida Anticipada`, `Descanso Laborado`.
* **Exportación:** Botones para descargar en formato **Excel (.xlsx)**, **PDF** o imprimir directamente.

---

### 6.2 Análisis de Ausentismo
Ubicación: **Reportes y análisis $\rightarrow$ Análisis de Ausentismo** (`/attendance/absenteeism-analysis`)

Permite visualizar métricas estadísticas y gráficas sobre inasistencias:
* Tasa global de ausentismo por periodo.
* Desglose de faltas agrupadas por departamento.
* Identificación de colaboradores con mayor recurrencia de faltas injustificadas.

---

### 6.3 Análisis de Retardos
Ubicación: **Reportes y análisis $\rightarrow$ Análisis de Retardos** (`/attendance/tardiness-analysis`)

Módulo analítico enfocado en la puntualidad organizacional:
* Minutos totales de retardo acumulados en la empresa.
* Top de departamentos y empleados con mayor índice de impuntualidad.
* Gráfico de dispersión de horas de llegada.

---

### 6.4 Impresión de Checadores (Tarjetas de Asistencia)
Ubicación: **Reportes y análisis $\rightarrow$ Impresión de Checadores** (`/attendance/cards`)

Genera el formato tradicional de **Tarjeta de Tiempo** quincenal o mensual para cada trabajador, optimizado para impresión:

* Encabezado con logotipo de la empresa, datos del colaborador, sucursal, departamento y periodo evaluado.
* Tabla día por día con Entrada, Salida, Horas Ordinarias, Horas Extras e Incidencias.
* Leyenda de conformidad y recuadros para firma física del trabajador y supervisor inmediato.
* Soporte para impresión en lote (un salto de página automático por empleado).

---

### 6.5 Reportes Avanzados
Ubicación: **Reportes y análisis $\rightarrow$ Reportes avanzados** (`/attendance/advanced-reports`)

Generación de reportes analíticos personalizados:
* Reporte especializado de **Horas Extras Acumuladas**.
* Reporte de **Días de Descanso y Festivos Laborados**.
* Reporte consolidado de tiempos muertos y permisos.

---

### 6.6 Logs de Asistencia en Bruto
Ubicación: **Reportes y análisis $\rightarrow$ Logs de Asistencia** (`/attendance/logs`)

Muestra la bitácora pura de marcaciones (`AttendanceRecord`) recolectadas directamente del hardware:
* ID del Empleado, Fecha y Hora exacta del evento.
* Tipo de verificación (Huella, Rostro, Tarjeta, Contraseña o Manual).
* Dispositivo de origen e IP.
* Estado de procesamiento interno (`Pending`, `Processed`, `Ignored`).

---

## 7. Configuración y Mantenimiento del Sistema

### 7.1 Configuración General y Parámetros Globales
Ubicación: **Configuración $\rightarrow$ Configuración** (`/settings`) *(Solo Administrador)*

Permite parametrizar las variables operativas de la plataforma:
* **Nombre de la Empresa y Razón Social.**
* **Tolerancia Global de Entrada (Minutos):** Valor por defecto para turnos que no definan tolerancia propia.
* **Tolerancia de Salida Anticipada (Minutos).**
* **Respaldos Automáticos:** Active el interruptor **Habilitar Respaldos Automáticos** y elija la **Hora de Ejecución** para generar cada día un respaldo completo. Viene **desactivado** de fábrica. Los respaldos se guardan en el directorio configurado en el módulo de Respaldos (ver 7.2).
* **Configuración de Correo Electrónico (SMTP):** Servidor, puerto, usuario, contraseña y SSL para el envío automático de notificaciones de asistencia y reportes.

---

### 7.2 Respaldo y Restauración de Base de Datos
Ubicación: **Configuración $\rightarrow$ Respaldo y Restauración** (`/backup`) *(Solo Administrador)*

Garantiza la seguridad y disponibilidad de la información histórica.

#### Directorio de Almacenamiento
Carpeta del servidor donde se guardan y se buscan los respaldos. Use **Seleccionar** para elegirla y **Guardar Directorio** para aplicarla.
* Por defecto es la carpeta `Backups` dentro de la carpeta de la aplicación web (`C:\Program Files\AttendanceSystem\Web\Backups`).
* **Recomendación:** use una carpeta en **otro disco** del servidor y copie los respaldos periódicamente fuera del servidor (otro equipo, NAS o almacenamiento externo). Un respaldo que sólo existe en el mismo disco que la base de datos no protege contra una falla de ese disco.

#### Crear Respaldo
Escriba opcionalmente una descripción y elija:
* **Respaldo Completo** (`Full_Backup_AAAAMMDD_HHMMSS.zip`): base de datos más el archivo de configuración `appsettings.json` y una ficha con la descripción.
* **Solo BD** (`DB_Backup_AAAAMMDD_HHMMSS.backup`): únicamente la base de datos.

Cada respaldo se verifica al terminar; si no se puede leer, se descarta y se muestra el error.

> [!WARNING]
> El **Respaldo Completo** incluye `appsettings.json`, que contiene la contraseña de la base de datos. Guarde y comparta esos archivos con el mismo cuidado que una contraseña. La restauración sólo recupera la base de datos: la configuración incluida sirve como referencia para reinstalar, no se aplica sola.

#### Retención (purga automática)
Después de crear un respaldo, el sistema conserva los **10 respaldos más recientes** generados por él mismo (los archivos `Full_Backup_*` y `DB_Backup_*`) y elimina los anteriores. La cantidad se configura con `Backup:KeepLastNBackups` (ver Manual Técnico).
* Los respaldos **subidos** o copiados a la carpeta con otro nombre **no se purgan**: elimínelos a mano cuando ya no se necesiten.
* Tampoco se purgan los respaldos `PreMigration_Backup_*`, que el sistema crea solo antes de actualizar la estructura de la base tras instalar una versión nueva. Consérvelos hasta comprobar que la versión nueva funciona bien y luego elimínelos a mano.
* La fecha que se usa es la del nombre del archivo, no la de copia o subida.

#### Listado de Respaldos
Muestra los respaldos de la carpeta (`.zip`, `.backup` y `.bak`) con tipo, tamaño y fecha. Acciones por respaldo: **Descargar** al equipo, **Restaurar** y **Eliminar**.

Un archivo de tipo **SafetySnapshot** (resaltado en amarillo) es la copia del estado previo que el sistema conserva cuando una restauración falla sin poder revertirse (ver más abajo). No lo elimine hasta haber recuperado el sistema.

#### Cargar Respaldo al Servidor
Sube un archivo `.zip`, `.backup` o `.bak` (hasta 1 GB) desde su equipo a la carpeta de respaldos para poder restaurarlo.
* El archivo se revisa por completo antes de aparecer en la lista; si está dañado o no es un respaldo de PostgreSQL, se rechaza y no queda nada en la carpeta.
* Si ya existe un respaldo con el mismo nombre, la carga se rechaza y el existente no se modifica: elimínelo o cambie el nombre del archivo.

#### Restaurar un Respaldo
**Restaurar reemplaza todos los datos actuales por los del respaldo.** Lo registrado después de la fecha del respaldo se pierde.

1. Avise a los usuarios: durante la restauración el sistema **no acepta cambios** (en ningún equipo, incluidos los clientes de escritorio), los relojes ADMS reciben "servicio no disponible" y reenvían sus checadas después, y las tareas programadas se pausan.
2. Haga clic en **Restaurar** sobre el respaldo y confirme. El panel en vivo muestra cada etapa:
   * Validación del archivo.
   * Espera a que terminen las operaciones en curso (hasta 60 s). Si alguna no termina, la restauración se cancela **sin modificar nada**; inténtelo de nuevo en unos minutos.
   * **Snapshot de seguridad** del estado actual.
   * Restauración de la base de datos en **una sola operación**: si falla en cualquier punto, la base de datos queda **exactamente como estaba**.
   * Verificación del resultado y, si el respaldo es de una versión anterior del sistema, actualización automática de su estructura.
3. Al terminar, **reinicie la aplicación** como se indica en pantalla.

Sólo puede haber una restauración a la vez: si otro administrador (o un cliente de escritorio) ya está restaurando, la nueva se rechaza.

**Resultados posibles:**
* ✓ **Restauración completada:** el sistema ya trabaja con los datos del respaldo.
* ✗ **"La base de datos no se modificó":** la restauración falló antes de aplicar cambios (archivo dañado, error del respaldo, operaciones en curso). Todo sigue como antes.
* ✗ **"Fue revertida exitosamente a su estado original":** el respaldo se aplicó pero no superó la verificación, o es de una **versión más nueva** del sistema (actualice la aplicación antes de restaurarlo). El sistema volvió automáticamente al estado previo.
* 🚨 **"FALLO CRÍTICO":** el respaldo se aplicó, no superó la verificación y tampoco se pudo volver al estado previo. Vea [Restauración con fallo crítico](#6-el-sistema-quedó-en-mantenimiento-tras-una-restauración-fallo-crítico).

---

### 7.3 Gestión de Usuarios y Roles
Ubicación: **Configuración $\rightarrow$ Usuarios** (`/users`) *(Solo Administrador)*

Permite administrar el personal con acceso a la plataforma web:
* **Crear Usuario:** Capture el Nombre de Usuario, Correo Electrónico, Contraseña inicial y asigne el Rol (`Administrador`, `Supervisor` o `Usuario`).
* **Editar / Cambiar Rol:** Modifique los roles asignados a una cuenta existente.
* **Restablecer Contraseña:** Permite asignar una nueva contraseña segura a un usuario en caso de olvido.
* **Activar / Desactivar:** Bloquee el acceso a colaboradores que hayan dejado de laborar en la empresa sin eliminar su historial de auditoría.

---

### 7.4 Monitoreo de Tareas Programadas (Hangfire)
Ubicación: **Configuración $\rightarrow$ Hangfire** (`/hangfire`) *(Solo Administrador)*

Abre el panel de control del motor de procesamiento en segundo plano **Hangfire**:
* **Trabajos Recurrentes:** Muestra las tareas automatizadas (descarga programada de checadores cada 10 min, cálculo nocturno de asistencias y, si se habilitó en **Configuración**, el respaldo automático `automated-database-backup` a la hora elegida).
* **Durante una restauración** las tareas se pausan y se reanudan al terminar. Tras una restauración con fallo crítico permanecen en pausa hasta recuperar el sistema.
* **Trabajos en Cola y Procesamiento:** Permite verificar si existen tareas pendientes, trabajos fallidos o reintentar sincronizaciones manualmente.

---

## 8. Resolución de Problemas Frecuentes

### 1. El reloj checador aparece como "Desconectado" en la web
* **Para relojes en modo SDK (Pull):**
  1. Compruebe que el reloj esté encendido y que el cable de red esté conectado (luz verde/ámbar en el puerto Ethernet).
  2. En una terminal de comandos, ejecute `ping [IP_DEL_RELOJ]` (ej. `ping 192.168.1.201`). Si no hay respuesta, contacte a TI para revisar la red local.
  3. Verifique que el servicio de Windows `AttendanceSystem.ZKTeco.Service` esté en ejecución (`services.msc`).
* **Para relojes en modo ADMS (Push):**
  1. Revise en el menú del reloj checador que la opción de **Servidor Cloud / ADMS** esté activa y apuntando a la IP y puerto correctos del servidor web (`8081`).
  2. Verifique que el firewall del servidor permita tráfico entrante en el puerto `8081` TCP.

### 2. La hora en las checadas aparece desfasada
* En el menú **Dispositivos**, localice el reloj checador, haga clic en opciones y seleccione **Sincronizar Hora**. Esto configurará la hora del servidor y asegurará que la zona horaria esté en `UTC-6` sin horario de verano.

### 3. Las horas extras no aparecen en el reporte de un empleado
* Ingrese a **Empleados**, edite al trabajador, vaya a la pestaña **Horario y Asistencia** y verifique que la casilla **Autorización de Horas Extras** esté activada. Tras activarla, ejecute el cálculo de asistencia en **Cálculo de Asistencia** para el periodo deseado.

### 4. Un empleado checó pero el sistema marca falta
* Verifique si la marcación llegó a **Logs de Asistencia** (`/attendance/logs`). Si está registrada pero no calculada, vaya a **Cálculo de Asistencia** y ejecute el cálculo para esa fecha específica.

### 5. Aparece "la base de datos está en modo de mantenimiento" al guardar
* Hay una restauración en curso, iniciada desde este u otro equipo. Espere a que termine: el aviso desaparece solo en pocos segundos después de que concluya. Los relojes reenvían automáticamente las checadas de ese lapso.
* Si el aviso dice **Failed** y no desaparece, vea los puntos 6 y 7.

### 6. El sistema quedó en mantenimiento tras una restauración (fallo crítico)
Ocurre sólo si una restauración se aplicó, no superó la verificación y además no se pudo volver al estado previo. Para proteger los datos, el sistema se queda bloqueado **incluso después de reiniciar**: no acepta cambios, no ejecuta tareas programadas ni actualiza la base de datos. Sí permite consultar e iniciar sesión.

Para recuperarlo:
1. Un administrador entra a **Respaldo y Restauración** (`/backup`).
2. En el listado aparece un archivo **SafetySnapshot** con la fecha de la restauración fallida: es el estado previo. Restáurelo (o restaure otro respaldo confiable).
3. Al completarse la restauración, el bloqueo se levanta en todos los equipos y las tareas se reanudan. Reinicie la aplicación.
4. Si la restauración del snapshot también falla, no reintente repetidamente: conserve el archivo y el mensaje de error, y contacte a soporte técnico (ver Manual Técnico, sección 4.4).

### 7. El sistema quedó en mantenimiento después de una actualización
Al arrancar tras instalar una versión nueva, el sistema respalda la base de datos y actualiza su estructura. Si no puede hacerlo con seguridad, arranca en mantenimiento (igual que en el punto anterior: permite consultar e iniciar sesión, pero no guardar cambios). Ocurre cuando:
* **No se pudo crear el respaldo previo** (falta espacio, carpeta de respaldos inaccesible, no se encuentra `pg_dump`). La base no se modificó.
* **Falló la actualización de la estructura.** La base quedó en el último paso que terminó bien, y el respaldo previo está disponible.
* **La base de datos es de una versión más nueva** que la instalada (se reinstaló una versión anterior).

Para recuperarlo:
1. Pida a soporte técnico que revise el log de la aplicación: la causa aparece en una línea marcada como `FTL`.
2. Corrija la causa (liberar espacio, instalar la versión correcta, etc.) y **reinicie la aplicación**: el sistema vuelve a intentar la actualización por sí solo.
3. Si soporte lo indica, restaure desde **Respaldo y Restauración** el respaldo con la descripción *Estado previo a una actualización del esquema*.

---

## 9. Preguntas Frecuentes (FAQ)

### ¿Se pueden conectar diferentes modelos de relojes checadores simultáneamente?
**Sí.** El sistema soporta simultáneamente dispositivos ZKTeco (tanto en modo SDK por red local como en modo ADMS Push para sucursales remotas) y dispositivos Hikvision mediante protocolo ISAPI.

### ¿Qué sucede si se interrumpe la conexión de red o internet?
Los relojes checadores continúan operando normalmente de forma autónoma almacenando las checadas en su memoria interna. En cuanto se restablece la comunicación, el sistema sincroniza automáticamente todos los registros pendientes sin pérdida de información.

### ¿Se pueden exportar los reportes para alimentar sistemas de nómina externos?
**Sí.** Todos los reportes cuentan con exportación nativa a **Excel (.xlsx)** estructurado, facilitando su importación o procesamiento en sistemas como CONTPAQi Nóminas, Aspel NOI, SAP u otros sistemas ERP.
