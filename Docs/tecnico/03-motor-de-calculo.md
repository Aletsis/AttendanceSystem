# Motor de cálculo de asistencia

Verificado contra el código en el commit `a019748` (versión `2.3.0`). Cada regla cita el archivo y la línea que la
implementan; las rutas son relativas a la raíz del repositorio.

**Todos los ejemplos numéricos de este documento son pruebas automáticas.** Están en
[MotorCalculoEjemplosTests.cs](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs),
con el mismo identificador (`R03`, `N04`…), y ejecutan el recálculo real (`ProcessDailyAttendanceCommandHandler`
con sus procesadores) sobre un almacén en memoria. Para comprobarlos:

```bash
dotnet test tests/AttendanceSystem.Application.UnitTests --filter "FullyQualifiedName~MotorCalculoEjemplosTests"
```

Resultado en este commit: 68 pruebas correctas. Si una regla cambia, su prueba falla y este documento debe
actualizarse con ella.

Condiciones de los ejemplos, salvo que el ejemplo diga otra cosa: semana del lunes 2026-08-03, zona
`America/Mexico_City` (UTC−6 todo el año), empleado con horas extra autorizadas, sin redondeo, sin tope, sin días
de descanso y sin configuración del sistema guardada. Sin configuración rigen los valores por omisión: ventana de
comida 12:30–15:00, 30 min de comida con goce, ventana de comida nocturna 23:00–04:00, doble toque de 15 min,
semana que empieza el lunes ([LunchPolicy.cs:18-27](../../src/Core/AttendanceSystem.Domain/Services/LunchPolicy.cs#L18-L27),
[WorkPeriodCalculator.cs:14-21](../../src/Core/AttendanceSystem.Domain/Services/WorkPeriodCalculator.cs#L14-L21)).

## Contenido

1. [Qué produce y cuándo se ejecuta](#1-qué-produce-y-cuándo-se-ejecuta)
2. [Paso 1: qué horario rige el día](#2-paso-1-qué-horario-rige-el-día)
3. [Paso 2: qué marcajes son de la jornada](#3-paso-2-qué-marcajes-son-de-la-jornada)
4. [Paso 3: qué procesador calcula](#4-paso-3-qué-procesador-calcula)
5. [Fórmulas comunes](#5-fórmulas-comunes)
6. [Dobles toques](#6-dobles-toques)
7. [Comida y salidas temporales](#7-comida-y-salidas-temporales)
8. [Horas extra: de bruto a efectivo](#8-horas-extra-de-bruto-a-efectivo)
9. [Reglas y ejemplos por tipo de turno](#9-reglas-y-ejemplos-por-tipo-de-turno)
10. [Decisiones de RH](#10-decisiones-de-rh)
11. [Comportamientos verificados que conviene conocer](#11-comportamientos-verificados-que-conviene-conocer)

---

## 1. Qué produce y cuándo se ejecuta

El cálculo de un empleado y una fecha produce una fila en `DailyAttendances` (la jornada) y una fila en
`PunchAssignments` por cada checada que la jornada usó, con su papel (entrada, salida, intermedia, doble toque…).
Ver [base de datos, 5.4](02-base-de-datos.md#54-cálculo-de-asistencia).

Lo ejecuta `ProcessDailyAttendanceCommandHandler`
([ProcessDailyAttendanceCommandHandler.cs:89-202](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L89-L202)).
Lo disparan la cola de consolidación al llegar checadas, la tarea diaria de las 00:05 y cada decisión de RH (ver
[arquitectura, sección 7](01-arquitectura.md#7-recorrido-de-una-checada)).

Reglas del recálculo:

| Regla | Fuente |
|---|---|
| El rango no puede pasar de 366 días; se valida antes de leer nada | [ProcessDailyAttendanceCommandHandler.cs:99-100](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L99-L100), [DateRangePolicy.cs:16](../../src/Core/AttendanceSystem.Domain/Services/DateRangePolicy.cs#L16) |
| Sólo empleados con periodo de empleo vigente en el rango, en orden por número de empleado | líneas 104-107 |
| Se procesan lotes de 25 empleados, cada lote en una transacción con bloqueo por empleado. Si un lote falla, se reprocesa uno por uno y sólo se revierte el empleado que falló | líneas 31, 136-181, 208-243 |
| Un día de un periodo cerrado no se recalcula ni se borra | líneas 427-430 |
| Un día de vacaciones, incapacidad o baja temporal no se calcula; si ya había jornada, se elimina y sus checadas quedan libres | líneas 438-444 |
| Cada jornada se reemplaza (se borra y se vuelve a crear) | líneas 539-540 |
| El recálculo se extiende al día anterior si su jornada puede tomar checadas del día pedido, y al siguiente si cambió qué checadas de ese día se usan | líneas 408-465 |
| Al final se recalculan las horas extra de las semanas completas que tocan el rango (y del periodo de nómina completo si el empleado tiene tope por periodo) | líneas 320-344, 362-379 |

## 2. Paso 1: qué horario rige el día

`AttendanceShiftResolutionService.Resolve` decide el turno y si el día es descanso, con esta precedencia
([AttendanceShiftResolutionService.cs:18-138](../../src/Core/AttendanceSystem.Domain/Services/AttendanceShiftResolutionService.cs#L18-L138)):

1. **Ajuste manual de RH para ese día** (`AttendanceAdjustments`): si indica descanso o turno, gana (líneas 47-62).
2. **Empleado rotativo** (tipo `Rotativo` vigente en esa fecha): el rol de turnos (`ShiftRosters`) del día da turno y
   descanso. Sin rol ese día, se autodetecta; si tampoco hay, se usa su horario fijo (líneas 76-88, 97-101).
3. **Empleado con horario fijo**: su horario. El rol de turnos no aplica a quien no es rotativo (línea 67).
4. **Empleado sin horario**: autodetección (líneas 89-94).

El descanso, si no lo decide RH, sale del rol (rotativos) o de los días de descanso vigentes del empleado en esa
fecha (línea 71). El horario y los descansos "vigentes" salen del historial de asignaciones del empleado
(`EmployeeScheduleAssignments`), no sólo de su ficha actual (`employee.AssignmentOn(date)`, línea 64).

**Autodetección** ([ShiftDetectionService.cs:15-53](../../src/Core/AttendanceSystem.Domain/Services/ShiftDetectionService.cs#L15-L53)):
toma el primer marcaje del día y busca, entre los turnos **matutino, vespertino y nocturno** registrados, el de
hora de inicio más cercana en un reloj de 24 h (23:30 está a 30 min de 00:00). Sólo acepta una distancia de hasta
**300 minutos** (5 h). Con empate gana el que empieza más temprano. Sin marcajes no hay autodetección
([AttendanceShiftResolutionService.cs:144-150](../../src/Core/AttendanceSystem.Domain/Services/AttendanceShiftResolutionService.cs#L144-L150)).
Ejemplos: [S01, S02](#95-sin-horario-y-autodetección), [M04](#94-mixto-y-rotativo).

**Turno mixto.** Un turno `Mixto` se sustituye por el horario que su plantilla asigna a ese día de la semana, y ese
horario rige el cálculo completo (líneas 104-135). Si al día le falta horario, la jornada se calcula **sin turno** y
queda una incidencia: *"El turno mixto 'X' no tiene horario para el miércoles: asígnele uno."* Ejemplos:
[M01, M02](#94-mixto-y-rotativo).

## 3. Paso 2: qué marcajes son de la jornada

Cada checada tiene una **fecha laboral** (`AttendanceRecords.WorkDate`). Una jornada toma sólo checadas **libres**
(que no usó otra jornada) de su fecha laboral, con dos ampliaciones
([ProcessDailyAttendanceCommandHandler.cs:552-609](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L552-L609),
[líneas 747-764](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L747-L764)):

- **Entrada anticipada del día anterior.** Si el turno empieza antes de las 02:00, la entrada se acepta desde 2 h
  antes de la hora programada aunque caiga el día anterior (líneas 592-599;
  [JornadaWindowPolicy.cs:25](../../src/Core/AttendanceSystem.Domain/Services/JornadaWindowPolicy.cs#L25)). No aplica
  a un flexible sin ventana de llegada. Ejemplo: [N05](#93-nocturno).
- **Día siguiente.** Los turnos que terminan después de medianoche, los nocturnos, los flexibles y los partidos
  también buscan checadas del día siguiente (líneas 584-589), cada uno con su ventana (sección 9).

Una checada que cae entre la salida programada de una jornada y la entrada programada de la siguiente pertenece a
la más cercana de las dos; dentro de la duración programada siempre es de la jornada en curso (líneas 616-625).

## 4. Paso 3: qué procesador calcula

([ProcessDailyAttendanceCommandHandler.cs:787-811](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/ProcessDailyAttendanceCommandHandler.cs#L787-L811))

| Si el horario del día es… | Procesador | Archivo |
|---|---|---|
| ninguno (sin turno) | Sin turno | [ProcessNoShiftAttendanceCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessNoShiftAttendanceCommand.cs) |
| `Flexible` | Flexible | [ProcessFlexibleAttendanceCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessFlexibleAttendanceCommand.cs) |
| `Partido` | Partido | [ProcessSplitAttendanceCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessSplitAttendanceCommand.cs) |
| `Nocturno`, **o cualquier otro cuya salida sea a la misma hora o antes que la entrada** (cruza medianoche) | Nocturno | [ProcessNightlyAttendanceCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessNightlyAttendanceCommand.cs) |
| `Matutino`, `Vespertino` (y el horario diario de un `Mixto`) que no cruzan medianoche | Regular | [ProcessRegularAttendanceCommand.cs](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessRegularAttendanceCommand.cs) |

Los procesadores eligen qué checadas son entrada, salida e intermedias. Las cuentas (retardo, trabajado, extra)
las hace siempre `DailyAttendance.CalculateStatusCore`
([DailyAttendance.cs:696-1039](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L696-L1039)).

**Hora de salida programada.** Un turno no guarda la salida que se captura: la calcula como
`inicio + horas de trabajo`
([Shift.cs:177](../../src/Core/AttendanceSystem.Domain/Aggregates/ShiftAggregate/Shift.cs#L177); flexible en la
línea 130). En un turno partido, `EndTime` es el fin del **primer** bloque y las horas de trabajo son la suma de los
dos bloques (líneas 135-165).

## 5. Fórmulas comunes

Notación: todas las horas son civiles (de la zona de la jornada) y en minutos. `⌈x⌉` es redondear hacia arriba.

| Símbolo | Significado |
|---|---|
| `Ep` | Entrada programada = fecha + hora de inicio del turno |
| `Sp` | Salida programada = `Ep` + horas de trabajo (día siguiente si cruza medianoche) |
| `E`, `S` | Entrada y salida reales, **con los segundos truncados** para el retardo ([DailyAttendance.cs:1078-1081](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1078-L1081)) |
| `T` | Tolerancia del turno |
| `D` | Minutos descontados = excedente de comida + salidas temporales pendientes o sin goce ([DailyAttendance.cs:154](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L154)) |

### 5.1 Retardo (`LateMinutes`)

```
retraso = E − Ep
LateMinutes = retraso   si retraso > T
            = 0         si no
```

Es el retraso **completo**, no lo que pasa de la tolerancia: con 15 min de tolerancia, llegar 20 min tarde son 20
min de retardo ([DailyAttendance.cs:933-943](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L933-L943)).
En flexible con ventana, `Ep` es el **fin** de la ventana (líneas 899-932); en flexible sin ventana no hay retardo.

### 5.2 Entrada de referencia (desde cuándo cuenta el tiempo trabajado)

Turnos que no son flexibles ([DailyAttendance.cs:1121-1152](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1121-L1152)):

```
si retraso ≤ T:
    Eref = E    si el empleado tiene "calcular extras antes de la entrada"
    Eref = Ep   si no
si retraso > T:
    k    = ⌈(retraso − T) / 30⌉
    Eref = Ep + 30·k
```

Es decir, después de la tolerancia el tiempo cuenta desde el siguiente bloque de 30 min que "repite" la tolerancia:
con entrada 08:00 y 15 min de tolerancia, de 08:16 a 08:45 cuenta desde 08:30; de 08:46 a 09:15, desde 09:00.
Ejemplos [R03, R04](#91-matutino-y-vespertino).

Flexible: `Eref = E`, o la entrada redondeada si el turno tiene redondeo (sección 9.7).

### 5.3 Tiempo trabajado, salida anticipada y excedente

Turnos regulares y nocturnos ([DailyAttendance.cs:1007-1036](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1007-L1036)):

```
TotalWorkedMinutes    = max(0, (S − Eref) − D)
salida requerida      = Sp + D
EarlyDepartureMinutes = salida requerida − S     si S < salida requerida, si no 0
excedente bruto       = TotalWorkedMinutes − (Sp − Ep)
```

Lo descontado **se repone al final**: quien excede la comida 20 min debe salir 20 min después, o queda con salida
anticipada (ejemplos [C02, C03](#92-comida-y-salidas-temporales)). La salida anticipada es un indicador: no se
resta otra vez del tiempo trabajado.

El tiempo trabajado sólo se calcula si hay entrada **y** salida. Con un solo marcaje es 0.

### 5.4 Falta, marcajes faltantes y descanso

([DailyAttendance.cs:713-725](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L713-L725),
[líneas 848-883](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L848-L883))

- **Descanso sin marcajes:** ni falta ni nada más; termina ahí.
- **Descanso con marcajes:** `WorkedOnRestDay = true` y la jornada se calcula **con el horario del día** como un día
  normal (ejemplo [R19](#91-matutino-y-vespertino)).
- **Día laborable sin ningún marcaje:** `IsAbsent = true`.
- **Entrada sin salida:** `MissingCheckOut`; **salida sin entrada:** `MissingCheckIn`.

## 6. Dobles toques

Dos marcajes a **15 minutos o menos** son un doble toque: cuenta uno solo y el otro queda auditado con el papel
`DiscardedDoubleTap` (7). El umbral se configura en `SystemConfiguration.DoubleTapMinutes`, hasta 60
([SystemConfiguration.cs:56-57](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L56-L57)).

- Si **todos** los marcajes del día están a 15 min o menos del primero, la jornada tiene un solo marcaje útil: se
  conserva el primero, o el último si el primero está más cerca de la salida que de la entrada
  ([ProcessRegularAttendanceCommand.cs:59-72](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessRegularAttendanceCommand.cs#L59-L72)).
- Un marcaje intermedio a 15 min o menos de la entrada o de la salida se descarta (líneas 104-123).
- Una ausencia intermedia de 15 min o menos también es doble toque
  ([LunchPolicy.cs:65](../../src/Core/AttendanceSystem.Domain/Services/LunchPolicy.cs#L65)).

Ejemplos [R15, R16](#91-matutino-y-vespertino).

**Un solo marcaje.** Se toma como entrada o salida según cuál de las dos programadas tenga más cerca; con empate,
salida ([ProcessRegularAttendanceCommand.cs:74-88](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessRegularAttendanceCommand.cs#L74-L88),
[líneas 195-201](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessRegularAttendanceCommand.cs#L195-L201)).
Ejemplos [R13, R14](#91-matutino-y-vespertino).

## 7. Comida y salidas temporales

Aplica a los turnos regulares, nocturnos y flexibles de tiempo corrido. El partido tiene su propia regla (9.6) y el
multi-marcaje no analiza ausencias (9.7).

### 7.1 Clasificación de los marcajes intermedios

Los marcajes entre la entrada y la salida se agrupan en pares en orden: (salida, regreso), (salida, regreso)…
([IntermediatePunchAnalysis.cs:14-55](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/IntermediatePunchAnalysis.cs#L14-L55)).
Cada par se clasifica así ([LunchPolicy.cs:50-75](../../src/Core/AttendanceSystem.Domain/Services/LunchPolicy.cs#L50-L75)):

| Condición, en este orden | Clasificación |
|---|---|
| El último marcaje sin pareja | **Sin pareja**: no se descuenta; queda la incidencia *"Marcaje intermedio sin pareja a las HH:mm…"* |
| Ausencia de 15 min o menos | Doble toque |
| Primera ausencia que **toca** la ventana de comida (basta que una parte caiga dentro) y la ventana está activa | **Comida** |
| Cualquier otra | **Salida temporal pendiente** de que RH la clasifique |

Ventanas: diurna para regulares y flexibles (por omisión 12:30–15:00) y nocturna para el procesador nocturno (por
omisión 23:00–04:00, puede cruzar medianoche). Ambas se encienden o apagan juntas con
`SystemConfiguration.LunchWindowEnabled`
([SystemConfiguration.cs:59-60](../../src/Core/AttendanceSystem.Domain/Aggregates/SystemConfigurationAggregate/SystemConfiguration.cs#L59-L60)).

### 7.2 Fórmulas

([DailyAttendance.cs:669-685](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L669-L685))

```
minutos con goce   = Shift.PaidLunchMinutes   si el turno lo define
                   = SystemConfiguration.PaidLunchMinutes   si no (30 por omisión)
LunchMinutesTaken  = suma de las ausencias clasificadas como comida
LunchExcessMinutes = max(0, LunchMinutesTaken − minutos con goce)
DeductedExitMinutes = suma de las salidas pendientes o sin goce
D                  = LunchExcessMinutes + DeductedExitMinutes
```

Cómo cuenta cada clasificación de una salida ([TemporaryExit.cs:19](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/TemporaryExit.cs#L19)):

| Clasificación | ¿Se descuenta? |
|---|---|
| Pendiente (0) | Sí, completa, mientras RH no la clasifique |
| Aprobada con goce (1) | No |
| Aprobada sin goce (2) | Sí, completa |
| Error de checada (3) | No |
| Comida (4) | Sólo el excedente sobre los minutos con goce |

El campo heredado `Shift.LunchBreakMinutes` ya no interviene en el cálculo
([Shift.cs:14-18](../../src/Core/AttendanceSystem.Domain/Aggregates/ShiftAggregate/Shift.cs#L14-L18)).

Una jornada con salidas pendientes o con incidencias queda **por revisar** (`RequiresReview`,
[DailyAttendance.cs:141-143](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L141-L143)).

## 8. Horas extra: de bruto a efectivo

Hay dos cifras por jornada: **bruto** (`RawOvertimeMinutes`, todo lo que pasó de la jornada) y **efectivo**
(`OvertimeMinutes`, lo que se paga). El efectivo lo calcula una sola vez `WeeklyHoursCalculationHelper.Calculate`
([WeeklyHoursCalculationHelper.cs:68-109](../../src/Core/AttendanceSystem.Application/Common/WeeklyHoursCalculationHelper.cs#L68-L109)),
en orden cronológico:

```
1. bruto     = excedente de la jornada (sección 5.3), o el de la bolsa semanal (8.2)
2. si la jornada no tiene extras autorizadas: efectivo = 0
3. si RH autorizó una cantidad para ese día: efectivo = esa cantidad, sólo si el bruto sigue siendo el que vio;
   si cambió, efectivo = 0 hasta que lo autorice de nuevo
   si no: efectivo = redondeo(bruto, método del empleado)
4. tope diario:     efectivo = min(efectivo, tope)
5. tope de periodo: efectivo = min(efectivo, tope − efectivo ya acumulado en el periodo)
```

Redondeo, siempre hacia abajo ([DailyAttendance.cs:1065-1076](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1065-L1076)):

| Método | Fórmula |
|---|---|
| 0 Sin redondeo | `bruto` |
| 1 Por media hora | `⌊bruto / 30⌋ · 30` |
| 2 Por hora | `⌊bruto / 60⌋ · 60` |

El periodo del tope es el de nómina que contiene la jornada (semanal, quincenal o mensual según
`SystemConfiguration.WorkPeriodMode`), no el rango que se recalcula
([WorkPeriodCalculator.cs:6-64](../../src/Core/AttendanceSystem.Domain/Services/WorkPeriodCalculator.cs#L6-L64)).
El tope del empleado sólo se guarda si tiene extras autorizadas
([Employee.cs:124](../../src/Core/AttendanceSystem.Domain/Aggregates/EmployeeAggregate/Employee.cs#L124)).

### 8.1 Autorización

La autorización es la de **cada jornada**: la del empleado, salvo que RH la cambie para ese día. Sin autorización el
bruto se conserva y aparece la nota *"Tiempo extra no autorizado: N min"*
([DailyAttendance.cs:186-191](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L186-L191)).
RH puede autorizar el excedente completo o bloques de 30 min que no lo pasen
([DailyAttendance.cs:100-101](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L100-L101));
ver [sección 10](#10-decisiones-de-rh).

### 8.2 Bolsa semanal

Un turno flexible con horas **semanales** (`Shift.WeeklyWorkHours`) no tiene salida anticipada ni extra diaria
([DailyAttendance.cs:978-984](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L978-L984)).
La extra se calcula por semana ([WeeklyHoursCalculationHelper.cs:114-187](../../src/Core/AttendanceSystem.Application/Common/WeeklyHoursCalculationHelper.cs#L114-L187)):

```
meta  = horas semanales · (días laborables con bolsa ÷ días laborables de la semana)
acumulado = suma de TotalWorkedMinutes de los días con bolsa, en orden
bruto del día = trabajado del día                     si el acumulado previo ya alcanzó la meta
              = max(0, acumulado previo + trabajado − meta)   si no
faltante = meta − acumulado, en el último día con bolsa, si la semana ya se calculó completa
```

Los días laborables son los que no son descanso. La meta se reduce proporcionalmente sólo por días fuera del periodo
de empleo, no por faltas ni permisos. Si la semana mezcla días con bolsa y días con horario diario, todo lo trabajado
en sus días laborables se compara con la meta combinada (bolsa proporcional + minutos programados de los días
diarios). Ejemplos [B01, B02](#98-bolsa-semanal).

## 9. Reglas y ejemplos por tipo de turno

En las tablas: **Marcajes** en hora civil (`+1` = día siguiente, `−1` = día anterior); **Ret** retardo, **Ant**
salida anticipada, **Trab** tiempo trabajado, **Bruto** y **Efect** horas extra, todo en minutos.

### 9.1 Matutino y vespertino

Turno de los ejemplos: matutino 08:00, 8 h (salida 16:00), 15 min de tolerancia. Procesador regular: con dos o más
marcajes, el primero es la entrada y el último la salida; los de en medio son intermedios
([ProcessRegularAttendanceCommand.cs:89-144](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessRegularAttendanceCommand.cs#L89-L144)).
Sólo toma marcajes de su fecha laboral (más la entrada anticipada).

| Ej. | Variante | Marcajes | Ret | Ant | Trab | Bruto | Efect | Cómo se obtiene |
|---|---|---|---|---|---|---|---|---|
| [R01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L110) | Puntual | 07:55 · 16:00 | 0 | 0 | 480 | 0 | 0 | Llegó antes: `Eref = Ep` = 08:00 → 16:00 − 08:00 |
| [R02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L118) | Retardo dentro de tolerancia | 08:15 · 16:00 | 0 | 0 | 480 | 0 | 0 | 15 ≤ 15 → sin retardo, `Eref` = 08:00 |
| [R03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L125) | Retardo fuera de tolerancia | 08:20 · 16:00 | 20 | 0 | 450 | 0 | 0 | 20 > 15 → retardo 20; `k = ⌈5/30⌉ = 1` → `Eref` = 08:30 |
| [R04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L133) | Retardo largo | 08:50 · 17:00 | 50 | 0 | 480 | 0 | 0 | `k = ⌈35/30⌉ = 2` → `Eref` = 09:00 → 17:00 − 09:00 |
| [R05](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L141) | Salida anticipada | 08:00 · 15:30 | 0 | 30 | 450 | 0 | 0 | 16:00 − 15:30 |
| [R06](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L148) | Tiempo extra | 08:00 · 18:10 | 0 | 0 | 610 | 130 | 130 | 610 − 480 |
| [R07](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L160) | Redondeo por media hora | 08:00 · 18:10 | 0 | 0 | 610 | 130 | 120 | `⌊130/30⌋·30` |
| R07 | Redondeo por hora | 08:00 · 18:10 | 0 | 0 | 610 | 130 | 120 | `⌊130/60⌋·60` |
| R07 | Por media hora | 08:00 · 18:50 | 0 | 0 | 650 | 170 | 150 | `⌊170/30⌋·30` |
| R07 | Por hora | 08:00 · 18:50 | 0 | 0 | 650 | 170 | 120 | `⌊170/60⌋·60` |
| [R08](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L167) | Extras no autorizadas | 08:00 · 18:00 | 0 | 0 | 600 | 120 | 0 | Nota *"Tiempo extra no autorizado: 120 min"* |
| [R09](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L175) | Tope diario 60 | 08:00 · 18:10 | 0 | 0 | 610 | 130 | 60 | `min(130, 60)` |
| [R10](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L182) | Tope semanal 120: lunes | 08:00 · 17:30 | 0 | 0 | 570 | 90 | 90 | Acumulado 0 → 90 |
| R10 | Tope semanal 120: martes | 08:00 · 17:30 | 0 | 0 | 570 | 90 | 30 | `min(90, 120 − 90)` |
| [R11](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L192) | Extras antes de la entrada (activo) | 07:00 · 16:00 | 0 | 0 | 540 | 60 | 60 | `Eref = E` = 07:00 |
| R11 | Mismo caso sin la opción | 07:00 · 16:00 | 0 | 0 | 480 | 0 | 0 | `Eref = Ep` = 08:00 |
| [R12](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L202) | Extras antes de la entrada, llega tarde dentro de tolerancia | 08:10 · 16:00 | 0 | 0 | 470 | 0 | 0 | `Eref = E` = 08:10 también al llegar tarde (sección 11) |
| [R13](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L210) | Sólo entrada | 08:02 | 0 | 0 | 0 | 0 | 0 | Más cerca de 08:00 que de 16:00 → entrada; `MissingCheckOut` |
| [R14](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L217) | Sólo un marcaje cerca de la salida | 15:50 | 0 | 10 | 0 | 0 | 0 | Es salida; `MissingCheckIn` |
| [R15](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L224) | Doble toque en la entrada | 08:00 · 08:05 · 16:00 | 0 | 0 | 480 | 0 | 0 | 08:05 queda como `DiscardedDoubleTap` |
| [R16](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L234) | Dos marcajes a 10 min | 08:00 · 08:10 | 0 | 0 | 0 | 0 | 0 | Un solo marcaje útil (08:00), entrada sin salida |
| [R17](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L241) | Falta | — | | | | | | `IsAbsent` |
| [R18](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L248) | Descanso sin marcajes (domingo) | — | | | | | | `IsRestDay`, no es falta |
| [R19](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L255) | Descanso trabajado | 08:00 · 14:00 | 0 | 120 | 360 | 0 | 0 | `WorkedOnRestDay`; se mide contra el horario 08:00–16:00 (sección 11) |
| [R20](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L263) | Vespertino 14:00–22:00 | 14:20 · 22:00 | 20 | 0 | 450 | 0 | 0 | Mismas reglas; `Eref` = 14:30 |
| [R21](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L270) | Vespertino 18:00–02:00 (cruza medianoche) | 18:00 · +1 02:00 | 0 | 0 | 480 | 0 | 0 | Lo calcula el procesador nocturno (sección 4) |

### 9.2 Comida y salidas temporales

Mismo turno matutino; ventana de comida 12:30–15:00 y 30 min con goce.

| Ej. | Variante | Marcajes | Comida | Exced. | Salida temp. | Trab | Ant | Cómo se obtiene |
|---|---|---|---|---|---|---|---|---|
| [C01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L280) | Comida de 30 | 08:00 · 13:00 · 13:30 · 16:00 | 30 | 0 | — | 480 | 0 | 30 ≤ 30 con goce |
| [C02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L287) | Comida de 50, sale a su hora | 08:00 · 13:00 · 13:50 · 16:00 | 50 | 20 | — | 460 | 20 | `D` = 20: 480 − 20; salida requerida 16:20 |
| [C03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L294) | Comida de 50, repone | 08:00 · 13:00 · 13:50 · 16:20 | 50 | 20 | — | 480 | 0 | 500 − 20; excedente 0 |
| [C04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L301) | Turno con 60 min con goce | 08:00 · 13:00 · 13:50 · 16:00 | 50 | 0 | — | 480 | 0 | `Shift.PaidLunchMinutes = 60` sustituye a la global |
| [C05](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L310) | Salida temporal pendiente | 08:00 · 10:00 · 10:40 · 16:00 | — | — | 40 pendiente | 440 | 40 | No toca la ventana; se descuenta; por revisar |
| [C06](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L319) | La misma, RH la aprueba con goce | ídem | — | — | 40 con goce | 480 | 0 | Deja de descontarse; ya no queda por revisar |
| [C07](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L334) | Dos ausencias en la ventana | 08:00 · 13:00 · 13:20 · 13:40 · 14:20 · 16:40 | 20 | 0 | 40 pendiente | 480 | 0 | La primera es comida; la segunda, salida temporal: 520 − 40 |
| [C08](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L341) | Ventana de comida apagada | 08:00 · 13:00 · 13:50 · 16:00 | 0 | 0 | 50 pendiente | 430 | — | Toda ausencia es salida temporal y se descuenta completa |
| [C09](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L352) | Intermedio sin pareja | 08:00 · 11:00 · 16:00 | — | — | — | 480 | 0 | Sin descuento; incidencia *"Marcaje intermedio sin pareja a las 11:00"* |

### 9.3 Nocturno

Turno de los ejemplos: 22:00, 8 h (salida 06:00 del día siguiente), 15 min de tolerancia. Ventanas del procesador
nocturno ([ProcessNightlyAttendanceCommand.cs:57-73](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessNightlyAttendanceCommand.cs#L57-L73)):

- **Entrada:** el marcaje más cercano a la entrada programada, desde **4 h antes** hasta el inicio de la ventana de
  salida.
- **Salida:** el **último** marcaje desde **2 h antes** de la salida programada hasta **7 h después**. La ventana se
  corta antes de la entrada de la jornada siguiente si ésta es laborable, sin recortar la salida programada.
- Sin marcaje en la ventana de salida, el último marcaje después de la entrada (que no sea doble toque) es la salida
  (líneas 123-130).
- La comida usa la ventana **nocturna** (23:00–04:00 por omisión).

| Ej. | Variante | Marcajes | Ret | Trab | Bruto | Otros | Cómo se obtiene |
|---|---|---|---|---|---|---|---|
| [N01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L362) | Puntual | 22:00 · +1 06:00 | 0 | 480 | 0 | | 06:00 − 22:00 |
| [N02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L369) | Retardo | 22:30 · +1 06:00 | 30 | 450 | 0 | | `k = ⌈15/30⌉ = 1` → `Eref` 22:30 |
| [N03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L376) | Tiempo extra | 22:00 · +1 08:00 | 0 | 600 | 120 | | 08:00 cae dentro de las 7 h posteriores |
| [N04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L383) | Comida nocturna de 45 | 22:00 · +1 02:00 · +1 02:45 · +1 06:15 | 0 | 480 | 0 | Comida 45, excedente 15, Ant 0 | 495 − 15; salida requerida 06:15 |
| [N05](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L390) | Nocturno 00:00–08:00 con entrada del día anterior | −1 23:50 · 08:00 | 0 | 480 | 0 | | Entrada anticipada (sección 3); `Eref` = 00:00 |

### 9.4 Mixto y rotativo

| Ej. | Variante | Resultado | Cómo se obtiene |
|---|---|---|---|
| [M01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L399) | Mixto: lunes matutino 08–16, martes vespertino 14–22. Lunes 08:00 · 16:00; martes 14:20 · 22:00 | Lunes: turno "Matutino 08-16", Trab 480. Martes: turno "Vespertino 14-22", Ret 20, Trab 450 | Cada día usa el horario de su plantilla con todas sus reglas; la jornada guarda el nombre del horario del día |
| [M02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L415) | Mixto sin horario para el miércoles. Miércoles 08:00 · 16:00 | Sin turno, Trab 480, incidencia *"…no tiene horario para el miércoles…"* | Se calcula como sin turno (9.5) |
| [M03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L426) | Rotativo con rol: lunes vespertino, martes descanso. Lunes 14:00 · 22:00 | Lunes: "Vespertino 14-22", Trab 480. Martes: descanso, no es falta | El rol del día da turno y descanso |
| [M04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L439) | Rotativo sin rol ese día. Miércoles 07:50 · 16:00 | "Matutino 08-16" autodetectado, Trab 480 | 07:50 está a 10 min de 08:00 y a 370 de 14:00 |

### 9.5 Sin horario y autodetección

Sin turno, sólo se registra el tiempo entre el **primer y el último marcaje del día** (de su fecha laboral): no hay
retardo, salida anticipada ni extra, porque no hay meta contra la cual medir
([DailyAttendance.cs:849-866](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L849-L866)).
La jornada lleva la nota *"Sin turno asignado: asígnele un horario"*.

| Ej. | Variante | Marcajes | Resultado | Cómo se obtiene |
|---|---|---|---|---|
| [S01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L449) | Sin horario; existen matutino 08:00 y vespertino 14:00 | 13:40 · 22:05 | "Vespertino 14-22" autodetectado, Trab 485, Bruto 5 | 13:40 a 20 min de 14:00; `Eref` = 14:00 → 22:05 |
| [S02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L456) | Igual, primer marcaje lejos | 19:30 · +1 03:00 | Sin turno, Bruto 0 | 19:30 está a 330 min de 14:00 (> 300) |
| [S03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L463) | Sin ningún turno registrado | 10:00 · 14:00 · 18:30 | Entrada 10:00, salida 18:30, Trab 510, sin retardo ni extra | Primero y último del día; el de en medio no se analiza |

### 9.6 Partido

Turno de los ejemplos: 09:00–13:00 y 15:00–19:00 (8 h), 10 min de tolerancia en cada bloque.

Asignación de marcajes ([ProcessSplitAttendanceCommand.cs:64-112](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessSplitAttendanceCommand.cs#L64-L112)):

- Busca marcajes hasta la salida programada del bloque 2 + 7 h. Si la entrada del día siguiente − 2,5 h cae después
  de esa salida programada y antes de esas 7 h, el límite es ese instante.
- Con **4 o más**: el primero es la entrada del bloque 1 y el último la salida del bloque 2; el descanso es el par de
  marcajes seguidos más cercano a 13:00 y 15:00.
- Con **menos de 4**: cada marcaje va, en orden, a la posición más cercana; con empate, la anterior. Las demás quedan
  faltantes.
- Los marcajes a mitad de un bloque siguen la regla de salidas temporales **sin ventana de comida**: el descanso
  entre bloques hace de comida (línea 176).

Fórmulas ([DailyAttendance.cs:728-846](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L728-L846)):

```
retardo  = retraso del bloque 1 (si > T1) + retraso del bloque 2 (si > T2)
anticip. = (salida B1 programada − salida B1 real, si salió antes)
         + (salida B2 programada + D − salida B2 real, si salió antes)
trabajado de cada bloque = salida real − entrada de referencia del bloque (regla de 5.2 con su tolerancia);
                           0 si al bloque le falta un marcaje
TotalWorkedMinutes = trabajado B1 + trabajado B2 − D
bruto = TotalWorkedMinutes − duración B1 − duración B2
```

El excedente de un bloque compensa lo que falte en el otro: sólo es extra lo que pasa de la jornada completa.

| Ej. | Variante | Marcajes | Ret | Ant | Trab | Bruto | Cómo se obtiene |
|---|---|---|---|---|---|---|---|
| [P01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L474) | Puntual | 09:00 · 13:00 · 15:00 · 19:00 | 0 | 0 | 480 | 0 | 240 + 240 |
| [P02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L481) | Retardo en el bloque 2 | 09:00 · 13:00 · 15:20 · 19:00 | 20 | 0 | 450 | 0 | B2 desde 15:30 (`k = ⌈10/30⌉ = 1`): 240 + 210 |
| [P03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L488) | Sale antes del bloque 1, repone en el 2 | 09:00 · 12:30 · 15:00 · 19:30 | 0 | 30 | 480 | 0 | 210 + 270; el exceso del bloque 2 compensa |
| [P04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L495) | Tiempo extra | 09:00 · 13:00 · 15:00 · 20:00 | 0 | 0 | 540 | 60 | 240 + 300 − 480 |
| [P05](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L502) | Tres marcajes | 09:00 · 13:00 · 19:00 | 0 | 0 | 240 | 0 | 19:00 es salida B2; falta la entrada B2 → el bloque 2 cuenta 0 |

Si el turno partido no tiene configurado el segundo bloque, se calcula como un solo bloque y queda la incidencia
*"Turno partido sin segundo bloque configurado: calculado como un solo bloque."*
([ProcessSplitAttendanceCommand.cs:48-55](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessSplitAttendanceCommand.cs#L48-L55),
[líneas 289-335](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessSplitAttendanceCommand.cs#L289-L335)).

### 9.7 Flexible

Un flexible define **horas diarias o semanales, no ambas**
([Shift.cs:112-128](../../src/Core/AttendanceSystem.Domain/Aggregates/ShiftAggregate/Shift.cs#L112-L128)), y puede
tener ventana de llegada (`HasEntryWindow`, de `StartTime` a `FlexWindowEndTime`) o entrada libre (en ese caso el
inicio se guarda como 00:00, línea 108).

**Tiempo corrido** (`PunchTrackingMode = 0`)
([ProcessFlexibleAttendanceCommand.cs:180-339](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessFlexibleAttendanceCommand.cs#L180-L339)):

- La entrada es el primer marcaje del día; la salida, el **último** marcaje de la ventana de salida, aunque caiga al
  día siguiente.
- Ventana de salida ([JornadaWindowPolicy.cs:32-44](../../src/Core/AttendanceSystem.Domain/Services/JornadaWindowPolicy.cs#L32-L44)):
  ancla = entrada programada, o la real si llegó tarde; fin = ancla + horas del turno + **4 h** (sin horas diarias,
  ancla + 24 h). Si el día siguiente es laborable, la ventana termina antes de la entrada de esa jornada, pero nunca
  antes de ancla + horas del turno.
- Fórmulas: `Eref = E` (o redondeada), `TotalWorkedMinutes = S − Eref − D`; salida esperada dinámica = `Eref` +
  horas + `D` ([DailyAttendance.cs:946-958](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L946-L958));
  con horas diarias, `Ant = horas − trabajado` si trabajó menos, o `bruto = trabajado − horas`
  ([líneas 985-1005](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L985-L1005)).
- Redondeo de entrada (si el turno lo tiene): bloque anterior de `RoundingInterval` min si la entrada está a no más de
  la tolerancia de él; si no, el bloque siguiente. La salida no se redondea
  ([DailyAttendance.cs:1083-1097](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1083-L1097)).

**Multi-marcaje** (`PunchTrackingMode = 1`) ([líneas 51-178](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessFlexibleAttendanceCommand.cs#L51-L178)):
los marcajes se toman en pares (entrada, salida) y se suman los intervalos cerrados. No hay análisis de comida ni de
salidas temporales: el tiempo entre intervalos simplemente no cuenta. Un marcaje impar abre un intervalo sin salida
que suma 0 y marca la jornada con salida faltante; antes se busca su salida en la ventana del día siguiente.

Turno con ventana de los ejemplos F01–F04: llegada 07:00–09:00, 8 h, sin tolerancia (F04: 5 min y redondeo de 15).

| Ej. | Variante | Marcajes | Ret | Ant | Trab | Bruto | Cómo se obtiene |
|---|---|---|---|---|---|---|---|
| [F01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L516) | Llega dentro de la ventana | 08:30 · 16:30 | 0 | 0 | 480 | 0 | Salida esperada dinámica 16:30 |
| [F02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L524) | Llega después de la ventana | 09:20 · 17:20 | 20 | 0 | 480 | 0 | Retardo contra el fin de la ventana (09:00); trabaja desde la llegada real |
| [F03](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L531) | Jornada corta | 08:00 · 15:00 | 0 | 60 | 420 | 0 | 480 − 420 |
| [F04](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L540) | Redondeo: entrada 08:04 | 08:04 · 16:00 | | | 480 | | 08:04 a 4 min de 08:00 (≤ 5) → 08:00 |
| F04 | Redondeo: entrada 08:07 | 08:07 · 16:15 | | | 480 | | 7 > 5 → 08:15 |
| [F05](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L547) | Sin ventana (entrada libre), 8 h | 13:00 · 21:30 | 0 | 0 | 510 | 30 | Sin retardo posible |
| [F06](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L555) | Multi-marcaje, 8 h | 08:00 · 12:00 · 13:00 · 17:30 | 0 | 0 | 510 | 30 | 240 + 270 |
| [F07](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L562) | Multi-marcaje impar | 08:00 · 12:00 · 13:00 | 0 | 240 | 240 | 0 | Intervalo abierto desde 13:00; `MissingCheckOut` e incidencia |
| [F08](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L570) | Guardia de 24 h, día siguiente de descanso | 08:00 · +1 08:30 | 0 | 0 | 1470 | 30 | Ventana hasta +1 12:00 (08:00 + 24 h + 4 h) |
| [F09](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L578) | Guardia de 24 h, día siguiente laborable | 08:00 · +1 08:30 | | | 0 | | La ventana termina a +1 08:00 (no antes de 08:00 + 24 h): 08:30 queda fuera y el lunes queda sin salida (sección 11) |

### 9.8 Bolsa semanal

Turno de los ejemplos: flexible sin ventana, 40 h semanales; empleado con descanso sábado y domingo; se recalcula la
semana completa (lunes a domingo).

| Ej. | Variante | Marcajes | Resultado | Cómo se obtiene |
|---|---|---|---|---|
| [B01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L591) | 9 h diarias de lunes a viernes | 08:00 · 17:00 cada día | Lunes a jueves: Bruto 0, Ant 0. Viernes: Trab 540, Bruto 300, Efect 300 | Meta 2400; acumulado al jueves 2160; viernes `2160 + 540 − 2400` |
| [B02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L603) | 456 min diarios (38 h) | 08:00 · 15:36 cada día | Faltante 120, registrado en el **domingo** | `2400 − 2280`; el último día con bolsa de la semana es el domingo (sección 11) |

## 10. Decisiones de RH

Las decisiones manuales se guardan en `AttendanceAdjustments` (una por empleado y día) y se aplican al final del
cálculo de la jornada, después de los procesadores
([DailyAttendance.cs:524-585](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L524-L585)).
Cada decisión queda en `AttendanceAdjustmentHistory` y recalcula de inmediato los días afectados
([AttendanceDecisionService.cs:164](../../src/Core/AttendanceSystem.Application/Features/Attendance/Decisions/AttendanceDecisionService.cs#L164),
[línea 201](../../src/Core/AttendanceSystem.Application/Features/Attendance/Decisions/AttendanceDecisionService.cs#L201)).

| Decisión | Efecto en el cálculo | Fuente |
|---|---|---|
| Turno del día | Sustituye el horario (paso 1, prioridad máxima) | [AttendanceShiftResolutionService.cs:56-61](../../src/Core/AttendanceSystem.Domain/Services/AttendanceShiftResolutionService.cs#L56-L61) |
| Descanso del día | Marca o quita el descanso | líneas 50-54 |
| Checada por posición | Para entrada, salida B1, entrada B2 y salida: asignar una checada o forzar vacío. Primero se vacían las posiciones retiradas y luego se colocan las asignadas | [DailyAttendance.cs:539-566](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L539-L566) |
| Clasificar una salida | Cambia si se descuenta (tabla de 7.2). Ejemplo [C06](#92-comida-y-salidas-temporales) | [DailyDecisionCommands.cs:13-43](../../src/Core/AttendanceSystem.Application/Features/Attendance/Decisions/DailyDecisionCommands.cs#L13-L43) |
| Autorizar extras del día, completas o parciales | Ver abajo | [DailyDecisionCommands.cs:56-100](../../src/Core/AttendanceSystem.Application/Features/Attendance/Decisions/DailyDecisionCommands.cs#L56-L100) |

**Autorización parcial.** Es válida si `0 < autorizados ≤ bruto` y además es el bruto completo o múltiplo de 30
([DailyAttendance.cs:100-101](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L100-L101)).
Si al recalcular el bruto cambia, la autorización deja de aplicar (efectivo 0) y aparece la nota *"El tiempo extra
cambió de X a Y min: autorícelo nuevamente"*.

| Ej. | Caso | Resultado |
|---|---|---|
| [A01](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L616) | Empleado sin extras autorizadas, 08:00 · 18:10 (bruto 130); RH autoriza 60 de 130 | Bruto 130, Efect 60 |
| [A02](../../tests/AttendanceSystem.Application.UnitTests/Features/Attendance/Documentation/MotorCalculoEjemplosTests.cs#L632) | Validez con bruto 130 | 130 sí (completo), 60 sí (2 bloques), 45 no (no es múltiplo de 30), 150 no (pasa del bruto) |

## 11. Comportamientos verificados que conviene conocer

Estos resultados salen de las pruebas de este documento. No se juzga aquí si son correctos para el negocio; se
revisarán en la fase de limitaciones.

1. **"Calcular extras antes de la entrada" también cuenta desde la llegada tardía.** Con la opción activa, quien
   llega 08:10 (dentro de la tolerancia) trabaja 470 min, no 480: la referencia es la llegada real en ambos sentidos
   ([R12](#91-matutino-y-vespertino); [DailyAttendance.cs:1148](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L1148)).
2. **El descanso trabajado se mide contra el horario normal.** Un domingo de descanso trabajado 08:00–14:00 registra
   120 min de salida anticipada y 0 de extra; no hay regla propia para el pago del descanso trabajado
   ([R19](#91-matutino-y-vespertino); [DailyAttendance.cs:714-725](../../src/Core/AttendanceSystem.Domain/Aggregates/DailyAttendanceAggregate/DailyAttendance.cs#L714-L725)).
3. **Un excedente de comida aparece en dos indicadores.** Se resta del tiempo trabajado y además genera salida
   anticipada si no se repone ([C02](#92-comida-y-salidas-temporales)). La salida anticipada no se vuelve a restar,
   pero un reporte que sume ambos contaría el mismo tiempo dos veces.
4. **Guardia de 24 h seguida de un día laborable.** Una salida marcada después de cumplir las 24 h queda fuera de la
   ventana y la guardia queda sin salida ni tiempo trabajado ([F09](#97-flexible)). Para guardias, el día siguiente debe
   ser descanso o la salida debe marcarse a más tardar a la hora de entrada.
5. **El faltante de la bolsa semanal se registra en el último día de la semana con bolsa**, aunque sea descanso
   (domingo en [B02](#98-bolsa-semanal)), y sólo cuando la semana se calculó hasta ese día
   ([WeeklyHoursCalculationHelper.cs:177-184](../../src/Core/AttendanceSystem.Application/Common/WeeklyHoursCalculationHelper.cs#L177-L184)).
6. **Multi-marcaje impar:** el intervalo abierto suma 0 y la jornada queda con salida anticipada por todo lo que
   falta ([F07](#97-flexible)).
7. **El retardo es el retraso completo**, no lo que excede la tolerancia (sección 5.1).
8. **En multi-marcaje, el redondeo de entrada se aplica sobre la hora UTC**
   ([ProcessFlexibleAttendanceCommand.cs:121-125](../../src/Core/AttendanceSystem.Application/Features/Attendance/Commands/ProcessDailyAttendance/SubCommands/ProcessFlexibleAttendanceCommand.cs#L121-L125)),
   no sobre la civil como en tiempo corrido. En zonas con desfase de horas completas (todo México) el resultado es el
   mismo; en una zona con desfase de media hora no lo sería. No hay prueba para este caso.
