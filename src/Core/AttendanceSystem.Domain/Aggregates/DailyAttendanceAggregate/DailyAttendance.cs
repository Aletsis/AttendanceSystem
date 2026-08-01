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
    public AttendanceSystem.Domain.Enumerations.ShiftType? ShiftType { get; private set; }
    public TimeSpan? ScheduledCheckIn { get; private set; }
    public TimeSpan? ScheduledCheckOut { get; private set; }
    public int ToleranceMinutes { get; private set; }
    public bool RoundingsEnabled { get; private set; }
    public int RoundingInterval { get; private set; }

    // Actual Data
    public DateTime? ActualCheckIn { get; private set; }
    public AttendanceRecordId? CheckInRecordId { get; private set; }
    public DateTime? ActualCheckOut { get; private set; }
    public AttendanceRecordId? CheckOutRecordId { get; private set; }
    
    // Calculated Status
    public bool IsAbsent { get; private set; }
    public int LateMinutes { get; private set; }
    public int EarlyDepartureMinutes { get; private set; }
    public int OvertimeMinutes { get; private set; } // Based on shift end or simple work hours?
    
    // Flags
    public bool MissingCheckIn { get; private set; }
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

    /// <summary>
    /// Texto dinámico para mostrar en reportes y exportaciones.
    /// Devuelve null si el día no tiene incidencias de salida temporal.
    /// </summary>
    public string? AttendanceNote => (HasTemporaryExits, TemporaryExitStatus) switch
    {
        (true, TemporaryExitStatus.Pending)        => $"⚠️ Salida temporal de {TemporaryExitMinutes} min — pendiente de clasificar",
        (true, TemporaryExitStatus.ApprovedPaid)   => $"✅ Permiso con goce — {TemporaryExitNote}",
        (true, TemporaryExitStatus.ApprovedUnpaid) => $"✂️ Permiso sin goce — {TemporaryExitMinutes} min descontados",
        (true, TemporaryExitStatus.Dismissed)      => $"ℹ️ Error de checada — ignorado",
        _                                          => null
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
        bool overtimeAuthorized = true)
    {
        var attendance = new DailyAttendance
        {
            Id = DailyAttendanceId.CreateUnique(),
            EmployeeId = employeeId,
            Date = date.Date,
            IsRestDay = isRestDay,
            CalculateOvertimeBeforeEntry = calculateOvertimeBeforeEntry,
            OvertimeAuthorized = overtimeAuthorized
        };

        // 1. Configure Shift Snapshot
        if (shift != null)
        {
            attendance.ShiftId = shift.Id;
            attendance.ShiftName = shift.Name;
            attendance.ShiftType = shift.ShiftType;
            
            var dayStartTime = shift.StartTime;
            var dayEndTime = shift.EndTime;

            if (shift.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Mixto)
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

    public void UpdateShift(Shift shift)
    {
        if (shift == null) throw new ArgumentNullException(nameof(shift));
        
        ShiftId = shift.Id;
        ShiftName = shift.Name;
        ShiftType = shift.ShiftType;

        var dayStartTime = shift.StartTime;
        var dayEndTime = shift.EndTime;

        if (shift.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Mixto)
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
        MissingCheckOut = false;
        WorkedOnRestDay = false;

        // If Rest Day
        if (IsRestDay)
        {
            if (ActualCheckIn.HasValue || ActualCheckOut.HasValue) 
            {
                 WorkedOnRestDay = true;
            }
            else
            {
                // If they did not punch anything on their rest day, they are just resting. Not absent.
                return;
            }
        }

        // Normal Day Logic
        if (ScheduledCheckIn == null || ScheduledCheckOut == null)
        {
            // Fallback for missing schedule details but working normal day
             if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
            {
                var totalMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;
                
                // If Rest Day, everything is overtime. If not, fallback to 8h (480m)
                int goal = IsRestDay ? 0 : 480;
                
                if (totalMinutes >= goal)
                {
                    OvertimeMinutes = (int)totalMinutes - goal;
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
        // Rule: Only late after tolerance. 
        if (ActualCheckIn.HasValue)
        {
            if (ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Continuo || ScheduledCheckIn == null)
            {
                // In Continuo or No-Shift mode, there are no lates.
                LateMinutes = 0;
            }
            else
            {
                var diff = (ActualCheckIn.Value - scheduledInDateTime).TotalMinutes;
                int delayMinutes = (int)diff;

                if (delayMinutes > ToleranceMinutes)
                {
                    LateMinutes = delayMinutes;
                }
            }
        }

        // EARLY DEPARTURE & OVERTIME
        if (ActualCheckOut.HasValue)
        {
            var scheduledOutDateTime = Date.Add(ScheduledCheckOut.Value);
            
             if (ScheduledCheckOut <= ScheduledCheckIn)
            {
                scheduledOutDateTime = scheduledOutDateTime.AddDays(1);
            }

            if (ActualCheckOut.Value < scheduledOutDateTime)
            {
                EarlyDepartureMinutes = (int)(scheduledOutDateTime - ActualCheckOut.Value).TotalMinutes;
            }

            // OVERTIME logic
            if (ActualCheckIn.HasValue)
            {
                // Calculate scheduled work duration
                var scheduledMinutes = (scheduledOutDateTime - scheduledInDateTime).TotalMinutes;

                // 1. Determine Reference Entry & Exit
                DateTime referenceEntry = GetReferenceEntry() ?? ActualCheckIn.Value;
                DateTime referenceExit = GetReferenceExit() ?? ActualCheckOut.Value;

                // 2. Calculate Worked Duration (Tiempo Laborado)
                var totalWorkedMinutes = (referenceExit - referenceEntry).TotalMinutes;

                // Deducir minutos de comida formal (turno con LunchBreakMinutes configurado)
                totalWorkedMinutes -= LunchBreakMinutesApplied;

                // Deducir minutos de permiso sin goce clasificado por el administrador
                if (TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid)
                    totalWorkedMinutes -= TemporaryExitMinutes;

                if (totalWorkedMinutes < 0) totalWorkedMinutes = 0;

                // 3. Calculate Overtime (Tiempo Extra)
                // Overtime = Worked Duration - Scheduled Duration
                double overtime = totalWorkedMinutes - scheduledMinutes;

                if (overtime > 0 && OvertimeAuthorized)
                {
                    OvertimeMinutes = (int)overtime;
                }
            }
        }
    }

    public static DateTime RoundEntry(DateTime checkIn, int roundingIntervalMinutes, int toleranceMinutes)
    {
        if (roundingIntervalMinutes <= 0) return checkIn;
        var prevBlock = new DateTime(checkIn.Year, checkIn.Month, checkIn.Day, checkIn.Hour, (checkIn.Minute / roundingIntervalMinutes) * roundingIntervalMinutes, 0, checkIn.Kind);
        var diff = (checkIn - prevBlock).TotalMinutes;
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
        if (ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Continuo)
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
            var delayMinutes = (ActualCheckIn.Value - scheduledInDateTime).TotalMinutes;
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
        if (ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Continuo)
        {
            if (RoundingsEnabled && RoundingInterval > 0)
            {
                return RoundExit(ActualCheckOut.Value, RoundingInterval);
            }
            return ActualCheckOut.Value;
        }
        return ActualCheckOut.Value;
    }
}
