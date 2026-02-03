# Cambios en el Cálculo de Horas Extra

## Resumen
Se modificó la lógica de cálculo de horas extra para que se calcule desde la **hora de entrada configurada** en el horario asignado al empleado hasta la **hora de salida real**, y solo cuando se hayan cumplido las horas laboradas programadas según el horario.

## Cambios Realizados

### 1. DailyAttendance.cs (Dominio)
**Archivo:** `src\Core\AttendanceSystem.Domain\Aggregates\DailyAttendanceAggregate\DailyAttendance.cs`

**Lógica Anterior:**
- Calculaba overtime como: `Horas Trabajadas - Horas Programadas`
- Donde Horas Trabajadas = `ActualCheckOut - ActualCheckIn`

**Nueva Lógica:**
- Calcula overtime como: `Tiempo desde ScheduledCheckIn hasta ActualCheckOut - Horas Programadas`
- **Validación:** Solo se calculan horas extra si el empleado trabajó al menos las horas programadas
- Fórmula: `overtime = (ActualCheckOut - ScheduledCheckIn) - (ScheduledCheckOut - ScheduledCheckIn)`

**Ejemplo:**
```
Horario Programado: 08:00 - 17:00 (9 horas)
Entrada Real: 08:15
Salida Real: 18:00

Lógica Anterior:
  Horas Trabajadas = 18:00 - 08:15 = 9h 45min
  Overtime = 9h 45min - 9h = 45min

Nueva Lógica:
  Horas Trabajadas = 18:00 - 08:15 = 9h 45min (cumple las 9h programadas ✓)
  Tiempo desde inicio programado = 18:00 - 08:00 = 10h
  Overtime = 10h - 9h = 1h
```

### 2. GetAdvancedAttendanceReportQueryHandler.cs (Aplicación)
**Archivo:** `src\Core\AttendanceSystem.Application\Features\Reports\Queries\GetAdvancedAttendanceReport\GetAdvancedAttendanceReportQueryHandler.cs`

Se actualizó el método `GetEffectiveOvertime()` para que sea consistente con la nueva lógica del dominio.

## Validaciones Implementadas

1. **Cumplimiento de Horas Laboradas:** Solo se calculan horas extra si `totalWorkedMinutes >= scheduledMinutes`
2. **Cálculo desde Hora Programada:** Se usa `ScheduledCheckIn` como punto de inicio, no `ActualCheckIn`
3. **Consistencia:** La misma lógica se aplica tanto en el dominio como en los reportes

## Impacto

- ✅ Los empleados que lleguen tarde pero se queden más tiempo para compensar, ahora tendrán horas extra calculadas correctamente
- ✅ Solo se otorgan horas extra cuando se cumplieron las horas laboradas completas
- ✅ El cálculo es más justo y refleja el tiempo extra trabajado desde el inicio del horario programado

## Archivos Modificados

1. `src\Core\AttendanceSystem.Domain\Aggregates\DailyAttendanceAggregate\DailyAttendance.cs`
2. `src\Core\AttendanceSystem.Application\Features\Reports\Queries\GetAdvancedAttendanceReport\GetAdvancedAttendanceReportQueryHandler.cs`

## Compilación

✅ El proyecto compila correctamente sin errores
✅ No se requieren cambios en la base de datos
✅ Los cambios son retrocompatibles con los datos existentes
