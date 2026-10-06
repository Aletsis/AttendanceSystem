using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

/// <summary>Estado de clasificación de las salidas temporales detectadas por el sistema.</summary>
public enum TemporaryExitStatus
{
    /// <summary>Detectada automáticamente, pendiente de revisión del administrador.</summary>
    Pending = 0,
    /// <summary>Clasificada como permiso con goce de sueldo. Sin deducción.</summary>
    ApprovedPaid = 1,
    /// <summary>Clasificada como permiso sin goce. Se deduce <see cref="DailyAttendance.TemporaryExitMinutes"/> del tiempo laborado.</summary>
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

    // Calculated Status
    public bool IsAbsent { get; private set; }
    public int LateMinutes { get; private set; }
    public int EarlyDepartureMinutes { get; private set; }
    public int OvertimeMinutes { get; private set; } // Based on shift end or simple work hours?
    public int TotalWorkedMinutes { get; private set; }
    public string? IntervalsData { get; private set; }
    public OvertimeCalculationMethod OvertimeCalculationMethod { get; private set; } = OvertimeCalculationMethod.NoRounding;

    // Flags
    public bool MissingCheckIn { get; private set; }
    public bool MissingBlock1CheckOut { get; private set; }
    public bool MissingBlock2CheckIn { get; private set; }
    public bool MissingCheckOut { get; private set; }
    public bool IsRestDay { get; private set; }
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
    /// <summary>Minutos de comida deducidos automáticamente al calcular la jornada.</summary>
    public int LunchBreakMinutesApplied { get; private set; }
    /// <summary>Indica si el turno fue detectado automáticamente por proximidad de marcaje.</summary>
    public bool IsAutoDetectedShift { get; private set; }

    /// <summary>
    /// Texto dinámico para mostrar en reportes y exportaciones.
    /// Devuelve null si el día no tiene incidencias de salida temporal ni turno autodetectado.
    /// </summary>
    public string? AttendanceNote => (HasTemporaryExits, TemporaryExitStatus, IsAutoDetectedShift) switch
    {
        (true, TemporaryExitStatus.Pending, true) => $"🔍 Turno autodetectado. ⚠️ Salida temporal de {TemporaryExitMinutes} min — pendiente de clasificar",
        (true, TemporaryExitStatus.Pending, false) => $"⚠️ Salida temporal de {TemporaryExitMinutes} min — pendiente de clasificar",
        (true, TemporaryExitStatus.ApprovedPaid, _) => $"✅ Permiso con goce — {TemporaryExitNote}",
        (true, TemporaryExitStatus.ApprovedUnpaid, _) => $"✂️ Permiso sin goce — {TemporaryExitMinutes} min descontados",
        (true, TemporaryExitStatus.Dismissed, _) => $"ℹ️ Error de checada — ignorado",
        (false, _, true) => "🔍 Turno detectado automáticamente por proximidad de marcaje",
        _ => null
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
        string? intervalsData = null)
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
            HasEntryWindow = hasEntryWindow
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
        IsRestDay = false;

        CalculateStatus();
    }

    public void SetRestDayOverride(bool isRestDay)
    {
        IsRestDay = isRestDay;
        CalculateStatus();
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
        int lunchMinutesDeducted,
        bool hasTemporaryExits,
        int temporaryExitMinutes)
    {
        LunchBreakMinutesApplied = lunchMinutesDeducted < 0 ? 0 : lunchMinutesDeducted;
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
        // Reset calculated fields
        IsAbsent = false;
        LateMinutes = 0;
        EarlyDepartureMinutes = 0;
        OvertimeMinutes = 0;
        MissingCheckIn = false;
        MissingBlock1CheckOut = false;
        MissingBlock2CheckIn = false;
        MissingCheckOut = false;
        WorkedOnRestDay = false;
        DynamicScheduledCheckOut = null;

        // If Rest Day
        if (IsRestDay)
        {
            if (ActualCheckIn.HasValue || ActualCheckOut.HasValue || ActualBlock1CheckOut.HasValue || ActualBlock2CheckIn.HasValue)
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

            // Retardo Bloque 1 (Tolerancia independiente: ToleranceMinutes)
            if (ActualCheckIn.HasValue)
            {
                var checkInNoSeconds = TruncateSeconds(ActualCheckIn.Value);
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
            if (ActualBlock2CheckIn.HasValue)
            {
                var block2InNoSeconds = TruncateSeconds(ActualBlock2CheckIn.Value);
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
            if (ActualBlock1CheckOut.HasValue)
            {
                if (ActualBlock1CheckOut.Value < schedB1Out)
                {
                    totalEarly += (int)(schedB1Out - ActualBlock1CheckOut.Value).TotalMinutes;
                }
            }

            // Salida anticipada Bloque 2
            if (ActualCheckOut.HasValue)
            {
                if (ActualCheckOut.Value < schedB2Out)
                {
                    totalEarly += (int)(schedB2Out - ActualCheckOut.Value).TotalMinutes;
                }
            }
            EarlyDepartureMinutes = totalEarly;

            // Cómputo de horas laboradas y horas extras
            double workedMinutesBlock1 = 0;
            if (ActualCheckIn.HasValue && ActualBlock1CheckOut.HasValue && ActualBlock1CheckOut.Value > ActualCheckIn.Value)
            {
                workedMinutesBlock1 = (ActualBlock1CheckOut.Value - ActualCheckIn.Value).TotalMinutes;
            }

            double workedMinutesBlock2 = 0;
            if (ActualBlock2CheckIn.HasValue && ActualCheckOut.HasValue && ActualCheckOut.Value > ActualBlock2CheckIn.Value)
            {
                workedMinutesBlock2 = (ActualCheckOut.Value - ActualBlock2CheckIn.Value).TotalMinutes;
            }

            double totalWorked = workedMinutesBlock1 + workedMinutesBlock2;

            if (TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid && TemporaryExitMinutes > 0)
            {
                totalWorked = Math.Max(0, totalWorked - TemporaryExitMinutes);
            }

            double scheduledDuration = (schedB1Out - schedB1In).TotalMinutes + (schedB2Out - schedB2In).TotalMinutes;

            if (totalWorked > scheduledDuration && OvertimeAuthorized)
            {
                OvertimeMinutes = (int)(totalWorked - scheduledDuration);
            }

            return;
        }

        // Normal Day Logic
        if (ScheduledCheckIn == null || ScheduledCheckOut == null)
        {
            // Fallback for missing schedule details but working normal day
            if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
            {
                var totalMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;

                // Jornada base estándar de 8 horas (480 min)
                int goal = 480;

                if (totalMinutes > goal && OvertimeAuthorized)
                {
                    OvertimeMinutes = (int)(totalMinutes - goal);
                }
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

        var scheduledInDateTime = Date.Add(ScheduledCheckIn.Value);

        // LATE Check (Retardo)
        // Rule: Only late after tolerance. Truncate seconds so that clocking in during the tolerance minute is within tolerance.
        if (ActualCheckIn.HasValue)
        {
            if (ShiftType == Enumerations.ShiftType.Continuo || ScheduledCheckIn == null)
            {
                // In Continuo or No-Shift mode, there are no lates.
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
                    var checkInNoSeconds = TruncateSeconds(ActualCheckIn.Value);

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
                var checkInNoSeconds = TruncateSeconds(ActualCheckIn.Value);
                var diff = (checkInNoSeconds - scheduledInDateTime).TotalMinutes;
                int delayMinutes = (int)diff;

                if (delayMinutes > ToleranceMinutes)
                {
                    LateMinutes = delayMinutes;
                }
            }
        }

        // Calcular salida esperada dinámica para turnos flexibles o continuos (solo en tiempo corrido)
        if (ShiftType == Enumerations.ShiftType.Continuo && ActualCheckIn.HasValue)
        {
            if (PunchTrackingMode == PunchTrackingMode.SingleInterval)
            {
                DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                var targetWorkHours = WorkHours ?? (ScheduledCheckOut.HasValue && ScheduledCheckIn.HasValue
                    ? (ScheduledCheckOut.Value >= ScheduledCheckIn.Value ? ScheduledCheckOut.Value - ScheduledCheckIn.Value : ScheduledCheckOut.Value.Add(TimeSpan.FromDays(1)) - ScheduledCheckIn.Value)
                    : TimeSpan.FromHours(8));
                DynamicScheduledCheckOut = referenceEntry.Add(targetWorkHours).AddMinutes(LunchBreakMinutesApplied);
            }
            else
            {
                DynamicScheduledCheckOut = null;
            }
        }
        else if (ShiftType == Enumerations.ShiftType.Flexible && ActualCheckIn.HasValue)
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
            if (ShiftType == Enumerations.ShiftType.Flexible || ShiftType == Enumerations.ShiftType.Continuo)
            {
                // Si es SingleInterval y TotalWorkedMinutes no fue precalculado por intervalos:
                if (PunchTrackingMode == PunchTrackingMode.SingleInterval && ActualCheckIn.HasValue && ActualCheckOut.HasValue)
                {
                    DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                    DateTime referenceExit = ActualCheckOut.Value;

                    var worked = (referenceExit - referenceEntry).TotalMinutes;
                    worked -= LunchBreakMinutesApplied;
                    if (TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid)
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

                        if (surplus > 0 && OvertimeAuthorized)
                        {
                            OvertimeMinutes = (int)ApplyOvertimeRounding(surplus, OvertimeCalculationMethod);
                        }
                        else
                        {
                            OvertimeMinutes = 0;
                        }
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

                if (ActualCheckOut.HasValue && ActualCheckOut.Value < scheduledOutDateTime)
                {
                    EarlyDepartureMinutes = (int)(scheduledOutDateTime - ActualCheckOut.Value).TotalMinutes;
                }

                if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
                {
                    double scheduledMinutes = (scheduledOutDateTime - scheduledInDateTime).TotalMinutes;
                    DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                    DateTime referenceExit = GetReferenceExit() ?? ActualCheckOut.Value;

                    var totalWorkedMinutes = (referenceExit - referenceEntry).TotalMinutes;
                    totalWorkedMinutes -= LunchBreakMinutesApplied;
                    if (TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid)
                        totalWorkedMinutes -= TemporaryExitMinutes;

                    if (totalWorkedMinutes < 0) totalWorkedMinutes = 0;
                    TotalWorkedMinutes = (int)totalWorkedMinutes;

                    double overtime = totalWorkedMinutes - scheduledMinutes;
                    if (overtime > 0 && OvertimeAuthorized)
                    {
                        OvertimeMinutes = (int)ApplyOvertimeRounding(overtime, OvertimeCalculationMethod);
                    }
                }
            }
        }
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

    public DateTime? GetReferenceEntry()
    {
        if (!ActualCheckIn.HasValue) return null;
        if (ShiftType == Enumerations.ShiftType.Continuo || ShiftType == Enumerations.ShiftType.Flexible)
        {
            if (RoundingsEnabled && RoundingInterval > 0)
            {
                return RoundEntry(ActualCheckIn.Value, RoundingInterval, ToleranceMinutes);
            }
            return ActualCheckIn.Value;
        }

        if (ScheduledCheckIn.HasValue)
        {
            var scheduledInDateTime = Date.Add(ScheduledCheckIn.Value);
            var checkInNoSeconds = TruncateSeconds(ActualCheckIn.Value);
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
                return CalculateOvertimeBeforeEntry ? ActualCheckIn.Value : scheduledInDateTime;
            }
        }
        return ActualCheckIn.Value;
    }

    public DateTime? GetReferenceExit()
    {
        if (!ActualCheckOut.HasValue) return null;
        if (ShiftType == Enumerations.ShiftType.Continuo || ShiftType == Enumerations.ShiftType.Flexible)
        {
            // Salida real para cómputo de horas laboradas y sobrante
            return ActualCheckOut.Value;
        }
        return ActualCheckOut.Value;
    }
}
