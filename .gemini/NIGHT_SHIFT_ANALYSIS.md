# Análisis de Problemas con Turnos Nocturnos

## Problema Reportado

Empleado de turno nocturno con los siguientes registros problemáticos:

### 30/01/26
- **6:04** - Marcado como "No válida" (debería ser **SALIDA**)
- **20:55** - Marcado como "Entrada" (✓ correcto)

### 31/01/26
- **6:20** - Marcado como "Entrada" (debería ser **SALIDA**)

### 01/02/26
- Sin logs (Empleado no checó)

### 02/02/26
- **20:51** - Marcado como "No válida" (En tarjetas aparece como salida del 31, debería ser **ENTRADA del 02**)
- **20:52** - Marcado como "Entrada" (debería ser **No válida** - doble checada)

### 03/02/26
- **6:09** - Marcado como "No válida" (debería ser **SALIDA**)

## Análisis del Código Actual

### Lógica de Procesamiento (ProcessDailyAttendanceCommandHandler.cs)

1. **Detección de Turno Nocturno** (líneas 128-135):
   - Se detecta cuando `shift.EndTime < shift.StartTime`
   - Se extiende la búsqueda al día siguiente: `searchEndDate = searchStartDate.AddDays(1)`

2. **Búsqueda de Registros** (línea 138):
   - Se obtienen registros desde `searchStartDate` hasta `searchEndDate`
   - Para turno nocturno, esto incluye 2 días

3. **Matching de Entrada** (líneas 172-184):
   - Busca el registro más cercano a `scheduledIn` (hora programada de entrada)
   - Tolerancia: 5 horas (300 minutos)
   - `scheduledIn = date.Add(shift.StartTime)` - Ejemplo: 30/01/26 20:55

4. **Matching de Salida** (líneas 186-217):
   - Busca el registro más cercano a `scheduledOut` (hora programada de salida)
   - Tolerancia: 16 horas (960 minutos)
   - `scheduledOut = date.Add(shift.EndTime).AddDays(1)` - Ejemplo: 31/01/26 06:00

## Problemas Identificados

### Problema 1: Registros del día anterior no se consideran para la salida del turno nocturno

**Escenario**: Turno nocturno 30/01/26 20:55 - 31/01/26 06:00

Cuando procesamos el **30/01/26**:
- `searchStartDate` = 30/01/26
- `searchEndDate` = 31/01/26
- `scheduledIn` = 30/01/26 20:55
- `scheduledOut` = 31/01/26 06:00

Registros encontrados:
- 30/01/26 20:55 → Coincide con entrada ✓
- 31/01/26 06:20 → Coincide con salida ✓

**PERO** cuando procesamos el **31/01/26**:
- `searchStartDate` = 31/01/26
- `searchEndDate` = 01/02/26
- `scheduledIn` = 31/01/26 20:55
- `scheduledOut` = 01/02/26 06:00

Registros encontrados:
- 31/01/26 06:20 → Este registro está dentro de la tolerancia de entrada (5 horas antes de 20:55)
- Se marca como ENTRADA cuando debería quedar como SALIDA del turno anterior

### Problema 2: Registros ya procesados se reutilizan

El código actual:
- Resetea los registros del día actual (líneas 81-105)
- Pero NO filtra registros ya procesados de otros días
- Comentario en línea 143: "NO. For Night Shifts... we must be able to 'steal' or 're-claim'"

Esto causa que:
- Un registro de salida (6:20 del 31/01) se "robe" como entrada del mismo día
- Registros de entrada (20:51 del 02/02) se marquen como salida del día anterior

### Problema 3: Lógica de tolerancia asimétrica no es suficiente

Tolerancias actuales:
- Entrada: ±5 horas (300 minutos)
- Salida: ±16 horas (960 minutos)

Para un turno de 20:55 a 06:00:
- Entrada programada: 20:55
  - Rango válido: 15:55 - 01:55 (del día siguiente)
- Salida programada: 06:00 (día siguiente)
  - Rango válido: 14:00 (día anterior) - 22:00 (día siguiente)

**El problema**: Un registro a las 6:20 está:
- Dentro del rango de salida del turno que empezó el día anterior ✓
- Dentro del rango de entrada del turno que empieza ese día ✗ (6:20 está a 14h35m de 20:55)

## Solución Propuesta

### Opción 1: Filtrar registros ya procesados de días anteriores

```csharp
var records = recordsEnumerable
    .Where(r => r.Status == AttendanceStatus.Pending || 
                r.CheckTime.Date == date.Date) // Solo permitir "robar" del mismo día
    .OrderBy(r => r.CheckTime)
    .ToList();
```

### Opción 2: Mejorar la lógica de matching para turnos nocturnos

Para turnos nocturnos, dividir la búsqueda en dos fases:

**Fase 1 - Buscar ENTRADA**:
- Solo buscar en el rango de la tarde/noche del día actual
- Rango: desde mediodía del día actual hasta medianoche

**Fase 2 - Buscar SALIDA**:
- Solo buscar en el rango de madrugada del día siguiente
- Rango: desde medianoche hasta mediodía del día siguiente
- SOLO si no está ya procesado

### Opción 3: Usar ventanas de tiempo específicas para entrada y salida

```csharp
if (isNightShift)
{
    // Para entrada: solo buscar desde 12:00 del día actual hasta 23:59
    var entryWindowStart = date.Date.AddHours(12);
    var entryWindowEnd = date.Date.AddDays(1).AddSeconds(-1);
    
    // Para salida: solo buscar desde 00:00 del día siguiente hasta 12:00
    var exitWindowStart = date.Date.AddDays(1);
    var exitWindowEnd = date.Date.AddDays(1).AddHours(12);
}
```

## Recomendación

Implementar **Opción 2 + Opción 3**: 
1. Usar ventanas de tiempo específicas para entrada y salida
2. Filtrar registros ya procesados EXCEPTO cuando estamos reprocesando explícitamente
3. Añadir validación adicional: la salida debe ser DESPUÉS de la entrada
