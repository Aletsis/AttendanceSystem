# 📗 Manual de Operaciones - Attendance System

Bienvenido al manual de usuario del **Attendance System**. Este manual está diseñado en un lenguaje accesible y práctico para guiar a los administradores de recursos humanos, supervisores de área, operadores de nómina y personal de seguridad en las tareas del día a día.

---

## 1. Introducción

El **Attendance System** es una solución corporativa que simplifica y automatiza el control de asistencia de los trabajadores, recolectando en tiempo real las checadas realizadas en los dispositivos biométricos (huellas, rostros y tarjetas) y procesándolas bajo las reglas de negocio de su empresa para simplificar el cálculo de la nómina.

### 1.1 Roles de Usuario y Permisos

Para mantener la seguridad y la confidencialidad de la información, el sistema cuenta con los siguientes perfiles de acceso:

| Rol de Usuario | Permisos Principales |
| :--- | :--- |
| **Administrador del Sistema** | <ul><li>Acceso completo a todos los módulos.</li><li>Configuración técnica de relojes checadores.</li><li>Gestión de usuarios del sistema y asignación de roles.</li><li>Modificación de políticas generales de tolerancia e incidencias.</li></ul> |
| **Operador de RH** | <ul><li>Alta, modificación y baja de empleados.</li><li>Asignación de turnos y horarios de trabajo.</li><li>Registro de justificaciones (vacaciones, incapacidades).</li><li>Generación y descarga de reportes para nómina.</li></ul> |
| **Supervisor de Área** | <ul><li>Consulta exclusiva de los registros del personal de sus departamentos a cargo.</li><li>Autorización de horas extras.</li><li>Revisión de justificaciones pendientes de aprobación.</li></ul> |

---

## 2. Primeros Pasos

### 2.1 Acceso Inicial a la Aplicación
1. Abra el navegador web en su computadora.
2. Ingrese la dirección URL proporcionada por su departamento de TI (ejemplo: `http://localhost:8081` o la dirección IP del servidor en su red local).
3. En la pantalla de inicio de sesión, introduzca las credenciales de administrador por defecto:
   * **Usuario:** `admin`
   * **Contraseña:** `Admin123!`
4. Presione el botón **Iniciar Sesión**.

### 2.2 Cambio de Contraseña Inicial
⚠️ **IMPORTANTE:** Por razones de seguridad corporativa, la primera vez que inicie sesión se le redirigirá automáticamente a la configuración de su perfil para cambiar la contraseña predeterminada.
* Su nueva contraseña debe incluir al menos: **6 caracteres, 1 letra mayúscula, 1 letra minúscula y 1 número**.

### 2.3 Recorrido por el Dashboard
Una vez dentro del sistema, la pantalla principal (Dashboard) le mostrará información en tiempo real:
* **Estado de Dispositivos:** Cantidad de relojes checadores que están "En Línea" y "Desconectados".
* **Resumen de Asistencia del Día:** Gráficos dinámicos con el porcentaje de empleados que ya checaron entrada, retardos acumulados y faltas del día actual.
* **Alertas Activas:** Notificaciones sobre pérdidas de conexión con los relojes checadores para una rápida respuesta técnica.

---

## 3. Gestión de Empleados

Este módulo permite administrar la base de datos de los trabajadores del sistema.

### 3.1 Alta de un Nuevo Empleado
1. En el menú lateral izquierdo, haga clic en **Empleados** y luego presione el botón **Nuevo Empleado**.
2. Complete la pestaña **Datos Personales**:
   * **Nombre(s)** y **Apellidos**.
   * **Correo electrónico** y **Teléfono** (opcionales).
   * **Fecha de Contratación** y **Género**.
3. Complete la pestaña **Estructura Organizacional**:
   * Asigne la **Sucursal**, **Departamento** y **Puesto** correspondiente.
4. Presione **Guardar**.

### 3.2 Enrolamiento Biométrico y de Acceso (Paso a Paso)
Una vez guardado el empleado en el sistema, es necesario registrar cómo se identificará físicamente ante los relojes checadores:

#### A. Registro de Tarjeta de Proximidad
1. Abra el registro del empleado en la web y navegue a la pestaña **Biometría y Acceso**.
2. Capture el número grabado en la tarjeta física en el campo **Número de Tarjeta**.
3. Guarde los cambios. El sistema enviará el número de tarjeta a todos los relojes de forma automática.

#### B. Registro de Huella Digital y Rostro en el Reloj Físico
1. Diríjase físicamente al reloj checador más cercano.
2. Acceda al menú del reloj presionando la tecla **M/OK** (si está bloqueado, requerirá que el administrador del sistema coloque su huella de administrador).
3. Vaya a **Usuarios** $\rightarrow$ **Gestionar** (o **Editar**).
4. Busque al empleado por su número de ID asignado.
5. Seleccione **Enrolar Huella** (Fingerprint) o **Enrolar Rostro** (Face).
6. Siga las instrucciones de la pantalla del reloj checador (colocar el dedo 3 veces consecutivas sobre el lector, o mirar fijamente a la cámara).
7. Guarde y salga. En la siguiente descarga automática o manual, las plantillas biométricas se respaldarán en la base de datos de la aplicación para seguridad.

### 3.3 Edición y Baja de Empleados
* **Baja de Empleado:** Para retirar a un empleado del sistema, abra su perfil, cambie el campo **Estado** a `Baja` y presione guardar. El sistema lo desactivará en la base de datos y ordenará al servicio la remoción del usuario en los relojes checadores en la próxima sincronización para impedir que continúe checando.

### 3.4 Importación Masiva de Empleados
Si requiere dar de alta a una gran cantidad de personal al inicio:
1. Vaya a **Empleados** y presione **Importar (Excel/CSV)**.
2. Descargue la plantilla de ejemplo disponible en el sistema.
3. Rellene las columnas respetando los encabezados (ID, Nombre, Apellidos, Correo, Sucursal, Departamento, etc.).
4. Cargue el archivo editado en la web del sistema y presione **Procesar Importación**.

---

## 4. Configuración de Horarios y Turnos

Para que el sistema determine si un trabajador llegó a tiempo o acumuló horas extras, necesita saber en qué horario debe trabajar.

### 4.1 Creación de Horarios (Turnos)
1. En el menú lateral, diríjase a **Horarios / Turnos**.
2. Haga clic en **Crear Turno** y asigne un nombre (ejemplo: *Turno Matutino Oficina*).
3. Defina los parámetros de tiempo:
   * **Hora de Entrada:** Hora oficial de inicio de actividades (ejemplo: `08:00`).
   * **Horas de Trabajo (Jornada):** Cantidad de horas a laborar (ejemplo: `09:00` horas para jornada de 8 horas + 1 hora de comida).
   * **Tolerancia (Minutos):** Minutos de gracia permitidos antes de registrar un retardo (ejemplo: `15` minutos, por lo que a las `08:16` ya se considera retardo).
4. Seleccione los días de la semana aplicables para el horario.
5. Presione **Guardar**.

### 4.2 Asignación de Horarios
* Abra el perfil del **Empleado**, vaya a la pestaña de configuración de horario y elija entre:
  * **Turno Fijo:** Asignar un horario predeterminado (ejemplo: *Turno Matutino*).
  * **Autorización de Horas Extras:** Marque la casilla correspondiente si el empleado tiene permitido acumular tiempo adicional; de lo contrario, cualquier checada fuera de su horario regular no generará pago de tiempo extra.

---

## 5. Gestión de Relojes Checadores

Desde este panel supervisará el hardware encargado de capturar las asistencias.

### 5.1 Panel de Dispositivos (Estado de Conexión)
En el menú **Dispositivos** verá una cuadrícula con las tarjetas de cada reloj checador:
* ✅ **En Línea (Verde):** El reloj está conectado a la red y comunicándose de forma correcta.
* ❌ **Desconectado / Sin Conexión (Gris):** El reloj no tiene energía, no hay red LAN, o la dirección IP configurada ha cambiado.
* ⚠️ **Error (Rojo):** Existe un fallo de comunicación interno en el reloj (memoria llena, fallo de credenciales, etc.).

### 5.2 Agregar un Reloj Checador
1. Haga clic en **Agregar Dispositivo**.
2. Ingrese los datos del reloj:
   * **ID Único:** Código corto de identificación (ejemplo: `RELOJ_PLANTA_1`).
   * **Nombre:** Nombre descriptivo (ejemplo: *Entrada Principal Recepción*).
   * **IP del Reloj:** Dirección de red estática asignada (ejemplo: `192.168.1.100`).
   * **Puerto:** Por defecto `4370` para ZKTeco.
   * **Marca:** Seleccione `ZKTeco` o `Hikvision`.
   * **Método de descarga:** `Sdk` (Pull - el sistema va por los datos) o `Adms` (Push - el reloj los envía).
3. Presione **Guardar**.

### 5.3 Sincronización Manual y Automática
* **Automática:** El sistema cuenta con un proceso en segundo plano que descarga de manera programada (ejemplo: cada 10 minutos) las asistencias de todos los relojes activos.
* **Manual:** Si necesita la información de manera urgente en la web antes de la hora programada, vaya al menú **Dispositivos**, seleccione el reloj y haga clic en **Descargar Registros Ahora**.

### 5.4 Solución de Problemas Comunes con Relojes
* **El Reloj aparece "Desconectado":**
  1. Revise que la pantalla del reloj físico esté encendida.
  2. Verifique que el cable de red ethernet esté bien conectado a la parte posterior del reloj.
  3. Ejecute un "ping" a la dirección IP del reloj desde una computadora de la red para comprobar conectividad.
* **La hora del reloj checador está desfasada (Incorrecta):**
  * En el panel de control de dispositivos, haga clic en el botón **Sincronizar Hora** para forzar al reloj checador a adoptar la hora exacta del servidor de la aplicación.

---

## 6. Registros de Asistencia

Muestra el historial y estado de las checadas diarias de su personal.

### 6.1 Consulta de Checadas
1. Diríjase a **Asistencias** $\rightarrow$ **Registros de Entrada/Salida**.
2. Utilice los filtros del panel superior para delimitar la búsqueda:
   * **Fecha de Inicio y Fin**.
   * **Departamento** o **Sucursal**.
   * Nombre o número de **Empleado**.
3. Presione **Buscar** para cargar la tabla de checadas en tiempo real.

### 6.2 Justificación de Incidencias y Creación de Registros Manuales
Si un empleado olvidó checar, llegó tarde justificadamente o no asistió por causas válidas:
1. En la fila de la incidencia o desde la ficha de asistencia del día del empleado, haga clic en **Justificar / Editar**.
2. **Si olvidó checar:** Ingrese la hora de checada faltante (registro manual).
3. **Si es una falta / retardo justificado:** Elija el tipo de justificación:
   * *Vacaciones*
   * *Incapacidad Médica*
   * *Permiso con goce de sueldo*
   * *Permiso sin goce de sueldo*
4. **Carga de Evidencia:** Suba un archivo PDF o imagen del justificante médico o documento oficial firmado.
5. Ingrese una nota explicativa y haga clic en **Aplicar Justificación**. El sistema recalculará la asistencia diaria eliminando el retardo o la falta para fines de nómina.

---

## 7. Incidencias y Excepciones

El sistema calcula de manera automática las incidencias diarias basándose en las checadas del personal contra su horario asignado.

### 7.1 Tipos de Incidencias Automáticas
* **Retardo:** Ocurre si el empleado realiza su checada de entrada después de la hora oficial más los minutos de tolerancia configurados.
* **Falta:** Se genera automáticamente al finalizar el día si el empleado no tiene un registro de entrada y no existe ninguna justificación cargada previamente.
* **Salida Anticipada:** Ocurre si la checada de salida se realiza antes de cumplir la jornada oficial de horas de trabajo establecidas en el turno.
* **Horas Extras:** Horas acumuladas después de cumplir con su jornada regular. Solo se calcularán para aquellos empleados que tengan activa la casilla de autorización en sus perfiles.

---

## 8. Reportes y Nómina

Este módulo le permite obtener resúmenes de datos listos para el cálculo de su pre-nómina.

### 8.1 Reportes Disponibles
* **Reporte de Asistencia General (Kardex):** Muestra de forma matricial las entradas, salidas e incidencias diarias de todos los trabajadores de un departamento durante un periodo.
* **Reporte de Faltas y Retardos:** Listado enfocado únicamente en las desviaciones de puntualidad y ausencias, útil para la aplicación de actas administrativas o descuentos.
* **Reporte de Horas Extras Detalladas:** Detalle de los minutos y horas excedentes laborados, con campos para que el supervisor autorice de forma individual qué horas pasan a pago.
* **Tarjeta de Tiempo Individual:** Resumen de un empleado en particular en un formato de hoja tamaño carta, ideal para impresión y firma física del trabajador.

### 8.2 Exportación de Datos
Todos los reportes generados en pantalla pueden exportarse haciendo clic en los botones superiores:
* 📥 **Exportar a Excel (.xlsx):** Ideal para realizar filtrados adicionales, análisis de datos o importaciones en su sistema de nómina.
* 📥 **Exportar a PDF:** Formato limpio y seguro listo para archivar de forma digital o imprimir.

---

## 9. Panel de Administración

*Módulo de uso exclusivo para Administradores de la aplicación.*

### 9.1 Configuración de la Empresa
* Vaya a **Administración** $\rightarrow$ **Configuración de Empresa**.
* Configure la Razón Social, cargue el logotipo de la empresa en formato PNG para personalizar los reportes impresos y ajuste la zona horaria del servidor.

### 9.2 Gestión de Usuarios
* **Agregar operadores del sistema:** Si necesita que más personal de RH o supervisores accedan a la aplicación, vaya a **Usuarios** $\rightarrow$ **Crear Usuario**, defina un nombre de usuario, contraseña provisional y asigne el Rol correspondiente (`Operador de RH` o `Supervisor`).

---

## 10. Resolución de Problemas para Usuarios

Guía de respuestas rápidas ante inconvenientes del día a día:

### 10.1 "El reloj no lee mi huella o no reconoce mi rostro"
* **Causa 1:** El dedo del trabajador está húmedo, muy seco, sucio o tiene alguna cortadura reciente.
  * *Solución:* Limpie el lector del reloj y el dedo. Si el error persiste, enrole un segundo dedo (dedo de respaldo) desde el menú del reloj.
* **Causa 2:** El empleado no ha sido enrolado de manera correcta en el dispositivo físico.
  * *Solución:* Verifique en la interfaz web si el ID del empleado fue enviado al reloj. Vuelva a realizar el proceso de enrolamiento en el dispositivo físico.

### 10.2 "Faltan checadas de un empleado en la web, pero él asegura que checó en el reloj"
1. Ingrese a **Dispositivos** y verifique que el reloj checador esté "En Línea".
2. Ejecute una descarga manual presionando el botón **Sincronizar Dispositivo** en el perfil de dicho reloj para asegurar que los datos viajen de inmediato.
3. Si la descarga finaliza con éxito pero no aparece el registro, verifique si la hora interna del reloj checador es correcta. Si el reloj tiene una hora del pasado o del futuro, las asistencias podrían estar guardándose en fechas incorrectas. Sincronice la hora del reloj con la del servidor.

---

## 11. Preguntas Frecuentes (FAQ)

### ¿Se pueden usar celulares o tabletas para entrar a la aplicación?
**Sí.** La aplicación cuenta con una interfaz web totalmente responsive adaptada a pantallas móviles. Podrá revisar asistencias, justificar incidencias y descargar reportes cómodamente desde su tableta o teléfono celular estando conectado a la red de la empresa.

### ¿Si el reloj checador se queda sin internet se pierden las asistencias?
**No.** Los relojes checadores biométricos cuentan con una memoria de almacenamiento interna local capaz de retener miles de registros de asistencia de forma offline. Al momento de restablecerse el internet o la red LAN local, el sistema recuperará de forma automática y transparente todos los registros retenidos en la memoria del dispositivo.

### ¿El sistema hace respaldos de información automáticamente?
**Sí.** El instalador configura de forma automática una tarea diaria en el servidor que genera copias de seguridad de la base de datos a las 23:59 horas. Estas copias se almacenan en el directorio local de instalación.

---

## INFORMACIÓN PENDIENTE POR PROPORCIONAR

Para complementar este manual de operaciones, solicite a su área de soporte técnico o TI los siguientes datos específicos:

1. **[INSERTAR: URL de la aplicación web en producción]:** Dirección web de acceso definitiva para que los usuarios la guarden en sus favoritos (ejemplo: `http://192.168.10.25:8081` o `http://asistencia.miempresa.local`).
2. **[INSERTAR: Formato exacto de archivo Excel para importación masiva]:** Descripción del formato y orden de las columnas requeridas para cargar los empleados desde Excel.
3. **[INSERTAR: Logotipo de la empresa]:** Imagen oficial que debe subirse al sistema en el módulo de empresa para la cabecera de los reportes.
4. **[INSERTAR: Contacto de soporte interno (teléfono/correo)]:** Teléfonos y correos del personal interno de TI que da soporte a las incidencias de los relojes checadores o accesos al sistema.
