using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

/// <summary>Estado de clasificación de las salidas temporales detectadas por el sistema.</summary>
public enum TemporaryExitStatus
{
    /// <summary>Detectada automáticamente, pendiente de revisión del administrador. Se descuenta mientras tanto.</summary>
    Pending = 0,
    /// <summary>Clasificada como permiso con goce de sueldo. Sin deducción.</summary>
    ApprovedPaid = 1,
    /// <summary>Clasificada como permiso sin goce. Se deduce <see cref="DailyAttendance.TemporaryExitMinutes"/> del tiempo laborado (igual que pendiente).</summary>
    ApprovedUnpaid = 2,
    /// <summary>Clasificada como error de doble checada. Se ignora sin deducción.</summary>
    Dismissed = 3
}

public sealed class DailyAttendance : AggregateRoot<DailyAttendanceId>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime Date { get; private set; }

    // Shift Snapshot
    public ShiftId? ShiftId { get; private set; }
    public string? ShiftName { get; private set; }
    public ShiftType? ShiftType { get; private set; }
    public TimeSpan? ScheduledCheckIn { get; private set; }
    public TimeSpan? ScheduledCheckOut { get; private set; }
    public int ToleranceMinutes { get; private set; }
    public bool RoundingsEnabled { get; private set; }
    public int RoundingInterval { get; private set; }
    public TimeSpan? FlexWindowEndTime { get; private set; }
    public TimeSpan? WorkHours { get; private set; }
    public TimeSpan? WeeklyWorkHours { get; private set; }
    public DateTime? DynamicScheduledCheckOut { get; private set; }
    public TimeSpan? ScheduledBlock2CheckIn { get; private set; }
    public TimeSpan? ScheduledBlock2CheckOut { get; private set; }
    public int? SecondBlockToleranceMinutes { get; private set; }
    public PunchTrackingMode PunchTrackingMode { get; private set; } = PunchTrackingMode.SingleInterval;
    public bool HasEntryWindow { get; private set; } = false;

    // Actual Data
    public DateTime? ActualCheckIn { get; private set; }
    public AttendanceRecordId? CheckInRecordId { get; private set; }
    public DateTime? ActualBlock1CheckOut { get; private set; }
    public AttendanceRecordId? Block1CheckOutRecordId { get; private set; }
    public DateTime? ActualBlock2CheckIn { get; private set; }
    public AttendanceRecordId? Block2CheckInRecordId { get; private set; }
    public DateTime? ActualCheckOut { get; private set; }
    public AttendanceRecordId? CheckOutRecordId { get; private set; }
    public string? TimeZoneId { get; private set; }

    public DateTime? CivilCheckIn => ActualCheckIn.HasValue ? ToCivil(ActualCheckIn.Value) : null;
    public DateTime? CivilCheckOut => ActualCheckOut.HasValue ? ToCivil(ActualCheckOut.Value) : null;
    public DateTime? CivilBlock1CheckOut => ActualBlock1CheckOut.HasValue ? ToCivil(ActualBlock1CheckOut.Value) : null;
    public DateTime? CivilBlock2CheckIn => ActualBlock2CheckIn.HasValue ? ToCivil(ActualBlock2CheckIn.Value) : null;

    /// <summary>La jornada tiene al menos un marcaje en alguna posición (asistió aunque le falte alguno).</summary>
    public bool HasPunches => ActualCheckIn.HasValue || ActualCheckOut.HasValue || ActualBlock1CheckOut.HasValue || ActualBlock2CheckIn.HasValue;

    // Calculated Status
    public bool IsAbsent { get; private set; }
    public int LateMinutes { get; private set; }
    public int EarlyDepartureMinutes { get; private set; }
    public int OvertimeMinutes { get; private set; } // Based on shift end or simple work hours?
    public int RawOvertimeMinutes { get; private set; }
    public int TotalWorkedMinutes { get; private set; }
    /// <summary>Caso 6 (f): minutos que faltaron para la meta de la bolsa semanal (sólo en el último día con bolsa).</summary>
    public int WeeklyShortfallMinutes { get; private set; }
    public string? IntervalsData { get; private set; }
    public OvertimeCalculationMethod OvertimeCalculationMethod { get; private set; } = OvertimeCalculationMethod.NoRounding;

    // Flags
    public bool MissingCheckIn { get; private set; }
    public bool MissingBlock1CheckOut { get; private set; }
    public bool MissingBlock2CheckIn { get; private set; }
    public bool MissingCheckOut { get; private set; }
    public bool IsRestDay { get; private set; }
    public bool IsRestDayOverridden { get; private set; }
    public bool HasManualAdjustments { get; private set; }
    public bool WorkedOnRestDay { get; private set; }
    public bool CalculateOvertimeBeforeEntry { get; private set; }
    public bool OvertimeAuthorized { get; private set; }

    // --- Salidas Temporales Detectadas ---
    /// <summary>Indica si se detectaron salidas intermedias que requieren clasificación.</summary>
    public bool HasTemporaryExits { get; private set; }
    /// <summary>Minutos totales de ausencias intermedias no clasificadas como comida formal.</summary>
    public int TemporaryExitMinutes { get; private set; }
    /// <summary>Estado de clasificación de las salidas temporales detectadas.</summary>
    public TemporaryExitStatus TemporaryExitStatus { get; private set; } = TemporaryExitStatus.Pending;
    /// <summary>Nota de auditoría: quién clasificó y cuándo.</summary>
    public string? TemporaryExitNote { get; private set; }
    /// <summary>Minutos de comida descontados del tiempo trabajado: la comida a descontar que RH no perdonó.</summary>
    public int LunchBreakMinutesApplied { get; private set; }
    /// <summary>Duración real de la comida marcada (0 si no hubo).</summary>
    public int LunchMinutesTaken { get; private set; }
    /// <summary>
    /// Minutos de comida sujetos a descuento: la comida completa con el descuento activado, 0 si está desactivado.
    /// (Nombre heredado de cuando sólo se descontaba el exceso sobre los minutos con goce.)
    /// </summary>
    public int LunchExcessMinutes { get; private set; }
    /// <summary>RH perdonó el descuento de la comida de esta jornada.</summary>
    public bool LunchExcessForgiven { get; private set; }
    /// <summary>Indica si el turno fue detectado automáticamente por proximidad de marcaje.</summary>
    public bool IsAutoDetectedShift { get; private set; }

    /// <summary>
    /// Incidencias del cálculo visibles para RH (F06-02 / F06-04): configuración incompleta, intervalo abierto, etc.
    /// No las borra el recálculo de estado; se reconstruyen con la jornada.
    /// </summary>
    public string? CalculationIssue { get; private set; }

    /// <summary>
    /// La jornada necesita revisión de RH: tiene incidencias de cálculo (marcaje sin pareja, intervalo abierto,
    /// configuración incompleta) o salidas temporales pendientes de clasificar.
    /// </summary>
    public bool RequiresReview =>
        !string.IsNullOrWhiteSpace(CalculationIssue) ||
        (HasTemporaryExits && TemporaryExitStatus == TemporaryExitStatus.Pending);

    /// <summary>
    /// Las salidas temporales se descuentan salvo que RH las clasifique como permiso con goce o error de checada.
    /// </summary>
    public bool IsTemporaryExitDeducted =>
        HasTemporaryExits && TemporaryExitStatus is TemporaryExitStatus.Pending or TemporaryExitStatus.ApprovedUnpaid;

    /// <summary>MultiInterval con un intervalo sin salida: se muestra como salida faltante (F06-02).</summary>
    public bool HasOpenInterval { get; private set; }

    public void MarkOpenInterval(string issue)
    {
        HasOpenInterval = true;
        AddCalculationIssue(issue);
        CalculateStatus();
    }

    public void AddCalculationIssue(string issue)
    {
        if (string.IsNullOrWhiteSpace(issue)) return;
        CalculationIssue = string.IsNullOrWhiteSpace(CalculationIssue) ? issue : $"{CalculationIssue} | {issue}";
        if (CalculationIssue.Length > 500) CalculationIssue = CalculationIssue[..500];
    }

    /// <summary>
    /// Notas de la jornada para todos los reportes (única fuente): salida anticipada, salida temporal, comida,
    /// turno autodetectado e incidencias de cálculo. Los estados (falta, retardo, marcajes faltantes, descanso
    /// trabajado) no son notas: se leen de sus propias banderas.
    /// </summary>
    public IReadOnlyList<string> Notes
    {
        get
        {
            var notes = new List<string>();
            if (IsWithoutShift) notes.Add("Sin turno asignado: asígnele un horario");
            if (EarlyDepartureMinutes > 0) notes.Add($"Salida anticipada de {EarlyDepartureMinutes} min");
            if (WeeklyShortfallMinutes > 0) notes.Add($"Faltaron {WeeklyShortfallMinutes} min para la meta semanal");
            if (TemporaryExitSummary is { } temporary) notes.Add(temporary);
            if (LunchNote is { } lunch) notes.Add(lunch);
            if (IsAutoDetectedShift) notes.Add("Turno detectado automáticamente por proximidad de marcaje");
            if (!string.IsNullOrWhiteSpace(CalculationIssue))
            {
                notes.AddRange(CalculationIssue.Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(i => $"Incidencia: {i}"));
            }
            return notes;
        }
    }

    /// <summary>
    /// Caso 10: se calculó sin turno (un hueco de configuración que la revisión de horarios reporta). Un descanso sin
    /// marcajes no lo es.
    /// </summary>
    public bool IsWithoutShift => ShiftId == null && !(IsRestDay && !HasPunches);

    /// <summary>Las notas en una sola línea (exportaciones).</summary>
    public string? AttendanceNote => Notes.Count == 0 ? null : string.Join(" | ", Notes);

    private string? LunchNote => LunchMinutesTaken <= 0
        ? null
        : LunchExcessMinutes <= 0
            ? $"Comida de {LunchMinutesTaken} min — sin descuento"
            : LunchExcessForgiven
                ? $"Comida de {LunchMinutesTaken} min — descuento perdonado por RH"
                : $"Comida de {LunchMinutesTaken} min — {LunchExcessMinutes} min descontados";

    private string? TemporaryExitSummary => !HasTemporaryExits ? null : TemporaryExitStatus switch
    {
        TemporaryExitStatus.Pending => $"Salida temporal de {TemporaryExitMinutes} min — descontados, pendiente de clasificar",
        TemporaryExitStatus.ApprovedPaid => $"Salida temporal de {TemporaryExitMinutes} min — permiso con goce",
        TemporaryExitStatus.ApprovedUnpaid => $"Salida temporal de {TemporaryExitMinutes} min — permiso sin goce, descontados",
        _ => $"Salida temporal de {TemporaryExitMinutes} min — error de checada, ignorada"
    };

    private DailyAttendance() { }

    public static DailyAttendance Create(
        EmployeeId employeeId,
        DateTime date,
        Shift? shift,
        DateTime? checkIn,
        DateTime? checkOut,
        bool isRestDay = false,
        AttendanceRecordId? checkInRecordId = null,
        AttendanceRecordId? checkOutRecordId = null,
        bool calculateOvertimeBeforeEntry = false,
        bool overtimeAuthorized = true,
        bool isAutoDetectedShift = false,
        PunchTrackingMode punchTrackingMode = PunchTrackingMode.SingleInterval,
        bool hasEntryWindow = false,
        OvertimeCalculationMethod overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding,
        int totalWorkedMinutes = 0,
        string? intervalsData = null,
        string? timeZoneId = null)
    {
        var attendance = new DailyAttendance
        {
            Id = DailyAttendanceId.CreateUnique(),
            EmployeeId = employeeId,
            Date = date.Date,
            IsRestDay = isRestDay,
            CalculateOvertimeBeforeEntry = calculateOvertimeBeforeEntry,
            OvertimeAuthorized = overtimeAuthorized,
            IsAutoDetectedShift = isAutoDetectedShift,
            OvertimeCalculationMethod = overtimeCalculationMethod,
            TotalWorkedMinutes = totalWorkedMinutes,
            IntervalsData = intervalsData,
            PunchTrackingMode = punchTrackingMode,
            HasEntryWindow = hasEntryWindow,
            TimeZoneId = timeZoneId
        };

        // 1. Configure Shift Snapshot
        if (shift != null)
        {
            attendance.ShiftId = shift.Id;
            attendance.ShiftName = shift.Name;
            attendance.ShiftType = shift.ShiftType;

            var dayStartTime = shift.StartTime;
            var dayEndTime = shift.EndTime;

            if (shift.ShiftType == Enumerations.ShiftType.Mixto)
            {
                var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == attendance.Date.DayOfWeek);
                if (dayConfig != null)
                {
                    dayStartTime = dayConfig.StartTime;
                    dayEndTime = dayConfig.EndTime;
                }
            }

            attendance.ScheduledCheckIn = dayStartTime;
            attendance.ScheduledCheckOut = dayEndTime;
            attendance.ToleranceMinutes = shift.ToleranceMinutes;
            attendance.RoundingsEnabled = shift.RoundingsEnabled;
            attendance.RoundingInterval = shift.RoundingInterval;
            attendance.FlexWindowEndTime = shift.FlexWindowEndTime;
            attendance.WorkHours = shift.WorkHours > TimeSpan.Zero ? shift.WorkHours : null;
            attendance.WeeklyWorkHours = shift.WeeklyWorkHours;
            attendance.ScheduledBlock2CheckIn = shift.SecondBlockStartTime;
            attendance.ScheduledBlock2CheckOut = shift.SecondBlockEndTime;
            attendance.SecondBlockToleranceMinutes = shift.SecondBlockToleranceMinutes;
            attendance.PunchTrackingMode = shift.PunchTrackingMode;
            attendance.HasEntryWindow = shift.HasEntryWindow;
        }

        // 2. Set Actual Times
        attendance.ActualCheckIn = checkIn;
        attendance.CheckInRecordId = checkInRecordId;
        attendance.ActualCheckOut = checkOut;
        attendance.CheckOutRecordId = checkOutRecordId;

        // 3. Status Calculation Logic
        attendance.CalculateStatus();

        return attendance;
    }

    public void SetTimeZone(string? timeZoneId)
    {
        TimeZoneId = timeZoneId;
        CalculateStatus();
    }

    public DateTime ToCivil(DateTime dt)
    {
        if (dt.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(TimeZoneId))
            return dt;

        var tz = ResolveZone(TimeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(dt, tz);
    }

    private static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            timeZoneId = "America/Mexico_City";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch { }
            }
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            }
            catch
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId("America/Mexico_City", out var defaultWin))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(defaultWin); } catch { }
                }
                return TimeZoneInfo.Utc;
            }
        }
    }

    public void SetIntervalsData(int totalWorkedMinutes, string? intervalsData)
    {
        TotalWorkedMinutes = totalWorkedMinutes;
        IntervalsData = intervalsData;
        CalculateStatus();
    }

    public void SetCheckIn(DateTime checkIn, AttendanceRecordId recordId)
    {
        ActualCheckIn = checkIn;
        CheckInRecordId = recordId;
        CalculateStatus();
    }

    public void RemoveCheckIn()
    {
        ActualCheckIn = null;
        CheckInRecordId = null;
        CalculateStatus();
    }

    public void SetBlock1CheckOut(DateTime checkOut, AttendanceRecordId recordId)
    {
        ActualBlock1CheckOut = checkOut;
        Block1CheckOutRecordId = recordId;
        CalculateStatus();
    }

    public void RemoveBlock1CheckOut()
    {
        ActualBlock1CheckOut = null;
        Block1CheckOutRecordId = null;
        CalculateStatus();
    }

    public void SetBlock2CheckIn(DateTime checkIn, AttendanceRecordId recordId)
    {
        ActualBlock2CheckIn = checkIn;
        Block2CheckInRecordId = recordId;
        CalculateStatus();
    }

    public void RemoveBlock2CheckIn()
    {
        ActualBlock2CheckIn = null;
        Block2CheckInRecordId = null;
        CalculateStatus();
    }

    public void SetCheckOut(DateTime checkOut, AttendanceRecordId recordId)
    {
        ActualCheckOut = checkOut;
        CheckOutRecordId = recordId;
        CalculateStatus();
    }

    public void RemoveCheckOut()
    {
        ActualCheckOut = null;
        CheckOutRecordId = null;
        CalculateStatus();
    }

    public void SetCalculatedOvertime(int overtimeMinutes)
    {
        if (RawOvertimeMinutes <= 0)
        {
            RawOvertimeMinutes = Math.Max(OvertimeMinutes, Math.Max(0, overtimeMinutes));
        }
        OvertimeMinutes = Math.Max(0, overtimeMinutes);
    }

    /// <summary>F06-06: resultado del motor de extras (bruto, efectivo y faltante semanal) para la jornada.</summary>
    public void SetCalculatedOvertime(int rawOvertimeMinutes, int effectiveOvertimeMinutes, int weeklyShortfallMinutes = 0)
    {
        RawOvertimeMinutes = Math.Max(0, rawOvertimeMinutes);
        OvertimeMinutes = Math.Max(0, effectiveOvertimeMinutes);
        WeeklyShortfallMinutes = Math.Max(0, weeklyShortfallMinutes);
    }

    public void UpdateShift(Shift shift, bool isAutoDetectedShift = false)
    {
        if (shift == null) throw new ArgumentNullException(nameof(shift));

        ShiftId = shift.Id;
        ShiftName = shift.Name;
        ShiftType = shift.ShiftType;
        IsAutoDetectedShift = isAutoDetectedShift;

        var dayStartTime = shift.StartTime;
        var dayEndTime = shift.EndTime;

        if (shift.ShiftType == Enumerations.ShiftType.Mixto)
        {
            var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == Date.DayOfWeek);
            if (dayConfig != null)
            {
                dayStartTime = dayConfig.StartTime;
                dayEndTime = dayConfig.EndTime;
            }
        }

        ScheduledCheckIn = dayStartTime;
        ScheduledCheckOut = dayEndTime;
        ToleranceMinutes = shift.ToleranceMinutes;
        RoundingsEnabled = shift.RoundingsEnabled;
        RoundingInterval = shift.RoundingInterval;
        FlexWindowEndTime = shift.FlexWindowEndTime;
        WorkHours = shift.WorkHours > TimeSpan.Zero ? shift.WorkHours : null;
        WeeklyWorkHours = shift.WeeklyWorkHours;
        ScheduledBlock2CheckIn = shift.SecondBlockStartTime;
        ScheduledBlock2CheckOut = shift.SecondBlockEndTime;
        SecondBlockToleranceMinutes = shift.SecondBlockToleranceMinutes;

        // If updating shift, it's likely not a Rest Day anymore unless strict override, but usually shift implies work day.
        if (!IsRestDayOverridden)
        {
            IsRestDay = false;
        }

        CalculateStatus();
    }

    public void SetRestDayOverride(bool isRestDay)
    {
        IsRestDay = isRestDay;
        IsRestDayOverridden = true;
        HasManualAdjustments = true;
        CalculateStatus();
    }

    public void ApplyAdjustment(AttendanceAdjustment adjustment, Shift? shiftOverride = null)
    {
        if (adjustment == null) return;

        if (adjustment.IsRestDayOverride.HasValue)
        {
            IsRestDay = adjustment.IsRestDayOverride.Value;
            IsRestDayOverridden = true;
        }

        if (shiftOverride != null)
        {
            UpdateShift(shiftOverride, isAutoDetectedShift: false);
        }

        // F05-01: cada posición puede tener asignación forzada o vacío forzado. Primero se vacían las
        // posiciones que la decisión retira y después se colocan las asignadas, para que mover un marcaje
        // de una posición a otra no lo deje en ambas.
        foreach (var position in AttendanceAdjustment.AllPositions)
        {
            var kind = adjustment.OverrideKind(position);
            var decided = adjustment.OverrideRecord(position);
            if (kind == PositionOverrideKind.ForcedEmpty)
            {
                ClearPosition(position);
            }
            if (decided != null)
            {
                foreach (var other in AttendanceAdjustment.AllPositions.Where(p => p != position && PositionRecord(p) == decided))
                {
                    ClearPosition(other);
                }
            }
        }

        foreach (var position in AttendanceAdjustment.AllPositions)
        {
            if (adjustment.OverrideKind(position) != PositionOverrideKind.Assigned) continue;
            var recordId = adjustment.OverrideRecord(position);
            var time = adjustment.OverrideTime(position);
            if (recordId == null || !time.HasValue) continue;
            SetPositionValue(position, time.Value, recordId);
        }

        if (adjustment.TemporaryExitStatus.HasValue)
        {
            TemporaryExitStatus = adjustment.TemporaryExitStatus.Value;
            if (!string.IsNullOrWhiteSpace(adjustment.TemporaryExitNote))
            {
                TemporaryExitNote = adjustment.TemporaryExitNote;
            }
        }

        if (adjustment.OvertimeAuthorizedOverride.HasValue)
        {
            OvertimeAuthorized = adjustment.OvertimeAuthorizedOverride.Value;
        }

        if (adjustment.CalculateOvertimeBeforeEntryOverride.HasValue)
        {
            CalculateOvertimeBeforeEntry = adjustment.CalculateOvertimeBeforeEntryOverride.Value;
        }

        if (adjustment.LunchExcessForgiven)
        {
            LunchExcessForgiven = true;
            LunchBreakMinutesApplied = 0;
        }

        HasManualAdjustments = true;
        CalculateStatus();
    }

    private AttendanceRecordId? PositionRecord(AttendancePosition position) => position switch
    {
        AttendancePosition.CheckIn => CheckInRecordId,
        AttendancePosition.Block1CheckOut => Block1CheckOutRecordId,
        AttendancePosition.Block2CheckIn => Block2CheckInRecordId,
        _ => CheckOutRecordId
    };

    private void ClearPosition(AttendancePosition position)
    {
        switch (position)
        {
            case AttendancePosition.CheckIn: ActualCheckIn = null; CheckInRecordId = null; break;
            case AttendancePosition.Block1CheckOut: ActualBlock1CheckOut = null; Block1CheckOutRecordId = null; break;
            case AttendancePosition.Block2CheckIn: ActualBlock2CheckIn = null; Block2CheckInRecordId = null; break;
            default: ActualCheckOut = null; CheckOutRecordId = null; break;
        }
    }

    private void SetPositionValue(AttendancePosition position, DateTime time, AttendanceRecordId recordId)
    {
        switch (position)
        {
            case AttendancePosition.CheckIn: ActualCheckIn = time; CheckInRecordId = recordId; break;
            case AttendancePosition.Block1CheckOut: ActualBlock1CheckOut = time; Block1CheckOutRecordId = recordId; break;
            case AttendancePosition.Block2CheckIn: ActualBlock2CheckIn = time; Block2CheckInRecordId = recordId; break;
            default: ActualCheckOut = time; CheckOutRecordId = recordId; break;
        }
    }

    public void UpdateOvertimeConfiguration(bool overtimeAuthorized, bool calculateOvertimeBeforeEntry)
    {
        OvertimeAuthorized = overtimeAuthorized;
        CalculateOvertimeBeforeEntry = calculateOvertimeBeforeEntry;
        CalculateStatus();
    }

    /// <summary>
    /// Aplica los resultados del análisis de registros intermedios realizado por
    /// <c>ProcessDailyAttendanceCommandHandler</c> y recalcula el estado del día.
    /// </summary>
    public void ApplyIntermediateAnalysis(
        int lunchMinutesTaken,
        int lunchExcessMinutes,
        bool hasTemporaryExits,
        int temporaryExitMinutes)
    {
        LunchMinutesTaken = Math.Max(0, lunchMinutesTaken);
        LunchExcessMinutes = Math.Max(0, lunchExcessMinutes);
        LunchBreakMinutesApplied = LunchExcessForgiven ? 0 : LunchExcessMinutes;
        HasTemporaryExits = hasTemporaryExits;
        TemporaryExitMinutes = temporaryExitMinutes < 0 ? 0 : temporaryExitMinutes;
        // Si hay salidas temporales nuevas, forzar estado Pending
        if (hasTemporaryExits)
            TemporaryExitStatus = TemporaryExitStatus.Pending;
        CalculateStatus();
    }

    /// <summary>
    /// El administrador clasifica manualmente las salidas temporales detectadas.
    /// Si se clasifica como <see cref="TemporaryExitStatus.ApprovedUnpaid"/>,
    /// los minutos de la salida se descuentan automáticamente del tiempo laborado.
    /// </summary>
    public void ClassifyTemporaryExit(TemporaryExitStatus status, string classifiedByUserName)
    {
        TemporaryExitStatus = status;
        TemporaryExitNote = status == TemporaryExitStatus.Dismissed
            ? $"Ignorado por {classifiedByUserName} el {DateTime.Now:dd/MM/yyyy HH:mm}"
            : $"Autorizado por {classifiedByUserName} el {DateTime.Now:dd/MM/yyyy HH:mm}";
        CalculateStatus();
    }

    private void CalculateStatus()
    {
        CalculateStatusCore();
        if (HasOpenInterval && !IsAbsent)
        {
            MissingCheckOut = true;
        }
    }

    private void CalculateStatusCore()
    {
        // Reset calculated fields
        IsAbsent = false;
        LateMinutes = 0;
        EarlyDepartureMinutes = 0;
        OvertimeMinutes = 0;
        RawOvertimeMinutes = 0;
        MissingCheckIn = false;
        MissingBlock1CheckOut = false;
        MissingBlock2CheckIn = false;
        MissingCheckOut = false;
        WorkedOnRestDay = false;
        DynamicScheduledCheckOut = null;
        // Sin intervalos precalculados (multi-marcaje), el tiempo trabajado se deriva aquí de las posiciones actuales.
        if (IntervalsData == null) TotalWorkedMinutes = 0;

        // If Rest Day
        if (IsRestDay)
        {
            if (HasPunches)
            {
                WorkedOnRestDay = true;
            }
            else
            {
                // If they did not punch anything on their rest day, they are just resting. Not absent.
                return;
            }
        }

        // ShiftType.Partido: Horario Partido / Doble Turno con 2 bloques y 4 marcajes obligatorios
        if (ShiftType == Enumerations.ShiftType.Partido)
        {
            if (ScheduledCheckIn == null || ScheduledCheckOut == null || ScheduledBlock2CheckIn == null || ScheduledBlock2CheckOut == null)
            {
                return;
            }

            // Ausencia total: ningún marcaje de los 4
            if (ActualCheckIn == null && ActualBlock1CheckOut == null && ActualBlock2CheckIn == null && ActualCheckOut == null)
            {
                IsAbsent = true;
                return;
            }

            // 4 marcajes obligatorios:
            MissingCheckIn = ActualCheckIn == null;
            MissingBlock1CheckOut = ActualBlock1CheckOut == null;
            MissingBlock2CheckIn = ActualBlock2CheckIn == null;
            MissingCheckOut = ActualCheckOut == null;

            var schedB1In = Date.Add(ScheduledCheckIn.Value);
            var schedB1Out = Date.Add(ScheduledCheckOut.Value);
            var schedB2In = Date.Add(ScheduledBlock2CheckIn.Value);
            var schedB2Out = ScheduledBlock2CheckOut.Value <= ScheduledBlock2CheckIn.Value
                ? Date.AddDays(1).Add(ScheduledBlock2CheckOut.Value)
                : Date.Add(ScheduledBlock2CheckOut.Value);

            int totalLate = 0;
            int totalEarly = 0;

            var b1InCivil = ActualCheckIn.HasValue ? ToCivil(ActualCheckIn.Value) : (DateTime?)null;
            var block1OutCivil = ActualBlock1CheckOut.HasValue ? ToCivil(ActualBlock1CheckOut.Value) : (DateTime?)null;
            var block2InCivil = ActualBlock2CheckIn.HasValue ? ToCivil(ActualBlock2CheckIn.Value) : (DateTime?)null;
            var b2OutCivil = ActualCheckOut.HasValue ? ToCivil(ActualCheckOut.Value) : (DateTime?)null;

            // Retardo Bloque 1 (Tolerancia independiente: ToleranceMinutes)
            if (b1InCivil.HasValue)
            {
                var checkInNoSeconds = TruncateSeconds(b1InCivil.Value);
                if (checkInNoSeconds > schedB1In)
                {
                    int delay = (int)(checkInNoSeconds - schedB1In).TotalMinutes;
                    if (delay > ToleranceMinutes)
                    {
                        totalLate += delay;
                    }
                }
            }

            // Retardo Bloque 2 (Tolerancia independiente: SecondBlockToleranceMinutes)
            if (block2InCivil.HasValue)
            {
                var block2InNoSeconds = TruncateSeconds(block2InCivil.Value);
                if (block2InNoSeconds > schedB2In)
                {
                    int delay = (int)(block2InNoSeconds - schedB2In).TotalMinutes;
                    int tol2 = SecondBlockToleranceMinutes ?? ToleranceMinutes;
                    if (delay > tol2)
                    {
                        totalLate += delay;
                    }
                }
            }
            LateMinutes = totalLate;

            // Salida anticipada Bloque 1
            if (block1OutCivil.HasValue)
            {
                if (block1OutCivil.Value < schedB1Out)
                {
                    totalEarly += (int)(schedB1Out - block1OutCivil.Value).TotalMinutes;
                }
            }

            // Salida anticipada Bloque 2
            if (b2OutCivil.HasValue)
            {
                if (b2OutCivil.Value < schedB2Out)
                {
                    totalEarly += (int)(schedB2Out - b2OutCivil.Value).TotalMinutes;
                }
            }
            EarlyDepartureMinutes = totalEarly;

            // Cómputo de horas laboradas (caso 8): cada bloque cuenta desde su entrada de referencia, igual que el
            // turno fijo (bloques de 30 min tras la tolerancia); un bloque incompleto cuenta 0.
            double workedMinutesBlock1 = 0;
            var schedB1Duration = (schedB1Out - schedB1In).TotalMinutes;
            if (b1InCivil.HasValue && block1OutCivil.HasValue && block1OutCivil.Value > b1InCivil.Value)
            {
                var refInB1 = BlockReferenceEntry(b1InCivil.Value, schedB1In, ToleranceMinutes);
                workedMinutesBlock1 = Math.Max(0, (block1OutCivil.Value - refInB1).TotalMinutes);
            }

            double workedMinutesBlock2 = 0;
            var schedB2Duration = (schedB2Out - schedB2In).TotalMinutes;
            if (block2InCivil.HasValue && b2OutCivil.HasValue && b2OutCivil.Value > block2InCivil.Value)
            {
                var refInB2 = BlockReferenceEntry(block2InCivil.Value, schedB2In, SecondBlockToleranceMinutes ?? ToleranceMinutes);
                workedMinutesBlock2 = Math.Max(0, (b2OutCivil.Value - refInB2).TotalMinutes);
            }

            double totalWorked = workedMinutesBlock1 + workedMinutesBlock2;

            if (IsTemporaryExitDeducted && TemporaryExitMinutes > 0)
            {
                totalWorked = Math.Max(0, totalWorked - TemporaryExitMinutes);
            }

            TotalWorkedMinutes = (int)totalWorked;

            // Caso 8 (8.7): el excedente de un bloque se compensa con lo que falte en el otro (retardo, salida
            // anticipada, salidas temporales o un bloque incompleto): sólo es extra lo que pase de la jornada.
            SetOvertimeFromSurplus(totalWorked - schedB1Duration - schedB2Duration);

            return;
        }

        // Normal Day Logic
        if (ScheduledCheckIn == null || ScheduledCheckOut == null)
        {
            // Sin turno ni marcajes en un día que no es de descanso: no se presentó.
            if (!HasPunches)
            {
                IsAbsent = true;
                return;
            }

            // Caso 10: sin turno sólo se registra el tiempo entre el primer y el último marcaje; no hay meta contra la
            // cual calcular extras hasta que RH asigne un horario.
            if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
            {
                var totalMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes - LunchBreakMinutesApplied;
                if (IsTemporaryExitDeducted)
                    totalMinutes -= TemporaryExitMinutes;
                TotalWorkedMinutes = (int)Math.Max(0, totalMinutes);
            }
            return;
        }

        // ABSENCE Check: No records at all
        if (ActualCheckIn == null && ActualCheckOut == null)
        {
            IsAbsent = true;
            return;
        }

        // MISSING PUNCHES Check
        if (ActualCheckIn != null && ActualCheckOut == null)
        {
            MissingCheckOut = true;
        }
        else if (ActualCheckIn == null && ActualCheckOut != null)
        {
            MissingCheckIn = true;
        }

        var checkInCivil = ActualCheckIn.HasValue ? ToCivil(ActualCheckIn.Value) : (DateTime?)null;
        var checkOutCivil = ActualCheckOut.HasValue ? ToCivil(ActualCheckOut.Value) : (DateTime?)null;

        var scheduledInDateTime = Date.Add(ScheduledCheckIn.Value);

        // LATE Check (Retardo)
        // Rule: Only late after tolerance. Truncate seconds so that clocking in during the tolerance minute is within tolerance.
        if (checkInCivil.HasValue)
        {
            if (ScheduledCheckIn == null)
            {
                // In No-Shift mode, there are no lates.
                LateMinutes = 0;
            }
            else if (ShiftType == Enumerations.ShiftType.Flexible)
            {
                if (!HasEntryWindow)
                {
                    // Entrada abierta (sin ventana): no genera retardos
                    LateMinutes = 0;
                }
                else
                {
                    // Ventana de llegada: No genera retardo si llega dentro de la ventana (StartTime .. FlexWindowEndTime)
                    var windowEnd = FlexWindowEndTime ?? ScheduledCheckIn.Value;
                    var windowEndDateTime = Date.Add(windowEnd);
                    var checkInNoSeconds = TruncateSeconds(checkInCivil.Value);

                    if (checkInNoSeconds > windowEndDateTime)
                    {
                        var diff = (checkInNoSeconds - windowEndDateTime).TotalMinutes;
                        int delayMinutes = (int)diff;

                        if (delayMinutes > ToleranceMinutes)
                        {
                            LateMinutes = delayMinutes;
                        }
                        else
                        {
                            LateMinutes = 0;
                        }
                    }
                    else
                    {
                        LateMinutes = 0;
                    }
                }
            }
            else
            {
                var checkInNoSeconds = TruncateSeconds(checkInCivil.Value);
                var diff = (checkInNoSeconds - scheduledInDateTime).TotalMinutes;
                int delayMinutes = (int)diff;

                if (delayMinutes > ToleranceMinutes)
                {
                    LateMinutes = delayMinutes;
                }
            }
        }

        // Calcular salida esperada dinámica para turnos flexibles (solo en tiempo corrido)
        if (ShiftType == Enumerations.ShiftType.Flexible && ActualCheckIn.HasValue)
        {
            if (WorkHours.HasValue && WorkHours.Value > TimeSpan.Zero && PunchTrackingMode == PunchTrackingMode.SingleInterval)
            {
                DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                DynamicScheduledCheckOut = referenceEntry.Add(WorkHours.Value).AddMinutes(LunchBreakMinutesApplied);
            }
            else
            {
                DynamicScheduledCheckOut = null;
            }
        }

        // EARLY DEPARTURE & OVERTIME
        if (ActualCheckOut.HasValue || (PunchTrackingMode == PunchTrackingMode.MultiInterval && TotalWorkedMinutes > 0))
        {
            if (ShiftType == Enumerations.ShiftType.Flexible)
            {
                // Si es SingleInterval y TotalWorkedMinutes no fue precalculado por intervalos:
                if (PunchTrackingMode == PunchTrackingMode.SingleInterval && ActualCheckIn.HasValue && ActualCheckOut.HasValue)
                {
                    DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                    // Ambos extremos en hora civil: la salida en UTC sumaba el desfase de la zona horaria.
                    DateTime referenceExit = GetReferenceExit() ?? ToCivil(ActualCheckOut.Value);

                    var worked = (referenceExit - referenceEntry).TotalMinutes;
                    worked -= LunchBreakMinutesApplied;
                    if (IsTemporaryExitDeducted)
                        worked -= TemporaryExitMinutes;

                    TotalWorkedMinutes = (int)Math.Max(0, worked);
                }

                // Esquema de bolsa semanal
                if (WeeklyWorkHours.HasValue && WeeklyWorkHours.Value > TimeSpan.Zero)
                {
                    // En esquema de bolsa semanal, no hay salida temprana diaria fija ni tiempo extra diario
                    EarlyDepartureMinutes = 0;
                    OvertimeMinutes = 0;
                }
                else // Esquema de bolsa diaria
                {
                    double scheduledMinutes = WorkHours.HasValue && WorkHours.Value > TimeSpan.Zero
                        ? WorkHours.Value.TotalMinutes
                        : (ScheduledCheckOut.HasValue && ScheduledCheckIn.HasValue
                            ? (ScheduledCheckOut.Value >= ScheduledCheckIn.Value ? ScheduledCheckOut.Value - ScheduledCheckIn.Value : ScheduledCheckOut.Value.Add(TimeSpan.FromDays(1)) - ScheduledCheckIn.Value).TotalMinutes
                            : 480);

                    if (TotalWorkedMinutes < scheduledMinutes)
                    {
                        EarlyDepartureMinutes = (int)(scheduledMinutes - TotalWorkedMinutes);
                        OvertimeMinutes = 0;
                    }
                    else
                    {
                        EarlyDepartureMinutes = 0;
                        double surplus = TotalWorkedMinutes - scheduledMinutes;

                        SetOvertimeFromSurplus(surplus);
                    }
                }
            }
            else
            {
                DateTime scheduledOutDateTime = Date.Add(ScheduledCheckOut.Value);
                if (ScheduledCheckOut <= ScheduledCheckIn)
                {
                    scheduledOutDateTime = scheduledOutDateTime.AddDays(1);
                }

                // La duración del turno es trabajo efectivo: la comida descontada se repone al final de la jornada
                // (turno 09:00–18:00 con 1 h de comida descontada termina a las 19:00).
                var jornadaEnd = scheduledOutDateTime.AddMinutes(LunchBreakMinutesApplied);
                if (checkOutCivil.HasValue && checkOutCivil.Value < jornadaEnd)
                {
                    EarlyDepartureMinutes = (int)(jornadaEnd - checkOutCivil.Value).TotalMinutes;
                }

                if (checkInCivil.HasValue && checkOutCivil.HasValue)
                {
                    double scheduledMinutes = (scheduledOutDateTime - scheduledInDateTime).TotalMinutes;
                    DateTime referenceEntry = GetReferenceEntry() ?? checkInCivil.Value;
                    DateTime referenceExit = GetReferenceExit() ?? checkOutCivil.Value;

                    var totalWorkedMinutes = (referenceExit - referenceEntry).TotalMinutes;
                    totalWorkedMinutes -= LunchBreakMinutesApplied;
                    if (IsTemporaryExitDeducted)
                        totalWorkedMinutes -= TemporaryExitMinutes;

                    if (totalWorkedMinutes < 0) totalWorkedMinutes = 0;
                    TotalWorkedMinutes = (int)totalWorkedMinutes;

                    SetOvertimeFromSurplus(totalWorkedMinutes - scheduledMinutes);
                }
            }
        }
    }

    /// <summary>
    /// F06-06: el excedente bruto se conserva siempre (base para autorizaciones diarias, bolsa semanal y topes);
    /// las extras efectivas sólo existen si la jornada está autorizada. El redondeo y los topes definitivos
    /// los aplica <c>WeeklyHoursCalculationHelper</c> una sola vez sobre el bruto.
    /// </summary>
    private void SetOvertimeFromSurplus(double surplus)
    {
        RawOvertimeMinutes = (int)Math.Max(0, surplus);
        OvertimeMinutes = OvertimeAuthorized && surplus > 0
            ? (int)ApplyOvertimeRounding(surplus, OvertimeCalculationMethod)
            : 0;
    }

    public static double ApplyOvertimeRounding(double minutes, OvertimeCalculationMethod method)
    {
        switch (method)
        {
            case OvertimeCalculationMethod.RoundByHalfHour:
                return Math.Floor(minutes / 30.0) * 30.0;
            case OvertimeCalculationMethod.RoundByHour:
                return Math.Floor(minutes / 60.0) * 60.0;
            default:
                return minutes;
        }
    }

    public static DateTime TruncateSeconds(DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, dateTime.Kind);
    }

    public static DateTime RoundEntry(DateTime checkIn, int roundingIntervalMinutes, int toleranceMinutes)
    {
        if (roundingIntervalMinutes <= 0) return checkIn;
        var checkInNoSeconds = TruncateSeconds(checkIn);
        var prevBlock = new DateTime(checkIn.Year, checkIn.Month, checkIn.Day, checkIn.Hour, (checkIn.Minute / roundingIntervalMinutes) * roundingIntervalMinutes, 0, checkIn.Kind);
        var diff = (checkInNoSeconds - prevBlock).TotalMinutes;
        if (diff <= toleranceMinutes)
        {
            return prevBlock;
        }
        else
        {
            return prevBlock.AddMinutes(roundingIntervalMinutes);
        }
    }

    public static DateTime RoundExit(DateTime checkOut, int roundingIntervalMinutes)
    {
        if (roundingIntervalMinutes <= 0) return checkOut;
        return new DateTime(checkOut.Year, checkOut.Month, checkOut.Day, checkOut.Hour, (checkOut.Minute / roundingIntervalMinutes) * roundingIntervalMinutes, 0, checkOut.Kind);
    }

    /// <summary>
    /// Caso 8: entrada de referencia de un bloque del turno partido con la regla del turno fijo. Dentro de la tolerancia
    /// cuenta desde la hora programada (o desde la llegada anticipada si se calculan extras antes de la entrada); después,
    /// en bloques de 30 min que repiten la tolerancia (llega 09:12 con 10 min → 09:30).
    /// </summary>
    private DateTime BlockReferenceEntry(DateTime checkInCivil, DateTime scheduledIn, int toleranceMinutes)
    {
        var delayMinutes = (TruncateSeconds(checkInCivil) - scheduledIn).TotalMinutes;
        if (delayMinutes <= toleranceMinutes)
        {
            return CalculateOvertimeBeforeEntry && checkInCivil < scheduledIn ? checkInCivil : scheduledIn;
        }
        var k = (int)Math.Ceiling((delayMinutes - toleranceMinutes) / 30.0);
        return scheduledIn.AddMinutes(k * 30);
    }

    public DateTime? GetReferenceEntry()
    {
        if (!ActualCheckIn.HasValue) return null;
        var checkInCivil = ToCivil(ActualCheckIn.Value);
        if (ShiftType == Enumerations.ShiftType.Flexible)
        {
            if (RoundingsEnabled && RoundingInterval > 0)
            {
                return RoundEntry(checkInCivil, RoundingInterval, ToleranceMinutes);
            }
            return checkInCivil;
        }

        if (ScheduledCheckIn.HasValue)
        {
            var scheduledInDateTime = Date.Add(ScheduledCheckIn.Value);
            var checkInNoSeconds = TruncateSeconds(checkInCivil);
            var delayMinutes = (checkInNoSeconds - scheduledInDateTime).TotalMinutes;
            if (delayMinutes > ToleranceMinutes)
            {
                double rawK = (delayMinutes - ToleranceMinutes) / 30.0;
                int k = (int)Math.Ceiling(rawK);
                if (k < 0) k = 0;
                return scheduledInDateTime.AddMinutes(k * 30);
            }
            else
            {
                return CalculateOvertimeBeforeEntry ? checkInCivil : scheduledInDateTime;
            }
        }
        return checkInCivil;
    }

    public DateTime? GetReferenceExit()
    {
        if (!ActualCheckOut.HasValue) return null;
        var checkOutCivil = ToCivil(ActualCheckOut.Value);
        if (ShiftType == Enumerations.ShiftType.Flexible)
        {
            // Salida real para cómputo de horas laboradas y sobrante
            return checkOutCivil;
        }
        return checkOutCivil;
    }
}
