using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

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
    public bool MissingCheckIn { get; private set; } // Omitio entrada
    public bool MissingCheckOut { get; private set; } // Omitio salida
    public bool IsRestDay { get; private set; }
    public bool WorkedOnRestDay { get; private set; }
    public bool CalculateOvertimeBeforeEntry { get; private set; }
    public bool OvertimeAuthorized { get; private set; }

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

                // 1. Determine Reference Entry (Entrada de Referencia)
                DateTime referenceEntry = ActualCheckIn.Value;
                
                if (ShiftType != AttendanceSystem.Domain.Enumerations.ShiftType.Continuo && ScheduledCheckIn.HasValue)
                {
                    var delayMinutes = (ActualCheckIn.Value - scheduledInDateTime).TotalMinutes;
                    if (delayMinutes > ToleranceMinutes)
                    {
                        // Lateness exceeding tolerance -> round up to next 30-minute block from scheduled start, giving tolerance in each block
                        double rawK = (delayMinutes - ToleranceMinutes) / 30.0;
                        int k = (int)Math.Ceiling(rawK);
                        if (k < 0) k = 0;
                        
                        referenceEntry = scheduledInDateTime.AddMinutes(k * 30);
                    }
                    else
                    {
                        if (CalculateOvertimeBeforeEntry)
                        {
                            referenceEntry = ActualCheckIn.Value;
                        }
                        else
                        {
                            referenceEntry = scheduledInDateTime;
                        }
                    }
                }

                // 2. Calculate Worked Duration (Tiempo Laborado)
                var totalWorkedMinutes = (ActualCheckOut.Value - referenceEntry).TotalMinutes;
                
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
}
