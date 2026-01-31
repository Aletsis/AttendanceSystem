using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

public sealed class DailyAttendance : AggregateRoot<DailyAttendanceId>
{
    public EmployeeId EmployeeId { get; private set; }
    public DateTime Date { get; private set; }
    
    // Shift Snapshot
    public ShiftId? ShiftId { get; private set; }
    public string? ShiftName { get; private set; }
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

    private DailyAttendance() { }

    public static DailyAttendance Create(
        EmployeeId employeeId,
        DateTime date,
        Shift? shift,
        DateTime? checkIn,
        DateTime? checkOut,
        bool isRestDay = false,
        AttendanceRecordId? checkInRecordId = null,
        AttendanceRecordId? checkOutRecordId = null)
    {
        var attendance = new DailyAttendance
        {
            Id = DailyAttendanceId.CreateUnique(),
            EmployeeId = employeeId,
            Date = date.Date,
            IsRestDay = isRestDay
        };

        // 1. Configure Shift Snapshot
        if (shift != null && !isRestDay)
        {
            attendance.ShiftId = shift.Id;
            attendance.ShiftName = shift.Name;
            attendance.ScheduledCheckIn = shift.StartTime;
            attendance.ScheduledCheckOut = shift.EndTime;
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
        ScheduledCheckIn = shift.StartTime;
        ScheduledCheckOut = shift.EndTime;
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

            // Rule: On rest days, overtime counts starting from 8 hours worked (480 minutes)
            if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
            {
                var totalMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;
                OvertimeMinutes = Math.Max(0, (int)totalMinutes - 480);
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
                if (totalMinutes >= 480)
                {
                    OvertimeMinutes = (int)totalMinutes - 480;
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
        // Example: Tol 5. 8:05:59 is OK (5 min). 8:06:00 is Late (6 min).
        // Logic: Check integer minute difference.
        if (ActualCheckIn.HasValue)
        {
            // Calculate delay in minutes
            // We use (int) to get full completed minutes, or just total minutes comparison
            // 8:05:59 - 8:00 = 5.98 min. (int) = 5. 5 > 5 is False.
            // 8:06:00 - 8:00 = 6.0 min. (int) = 6. 6 > 5 is True.
            var diff = (ActualCheckIn.Value - scheduledInDateTime).TotalMinutes;
            int delayMinutes = (int)diff;

            if (delayMinutes > ToleranceMinutes)
            {
                LateMinutes = delayMinutes;
            }
        }

        // EARLY DEPARTURE & OVERTIME
        if (ActualCheckOut.HasValue)
        {
            var scheduledOutDateTime = Date.Add(ScheduledCheckOut.Value);
            
             if (ScheduledCheckOut < ScheduledCheckIn)
            {
                scheduledOutDateTime = scheduledOutDateTime.AddDays(1);
            }

            if (ActualCheckOut.Value < scheduledOutDateTime)
            {
                EarlyDepartureMinutes = (int)(scheduledOutDateTime - ActualCheckOut.Value).TotalMinutes;
            }

            // OVERTIME logic
            // Rule: Only assign overtime if 8 hours were worked.
            if (ActualCheckIn.HasValue)
            {
                var totalWorkedMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;

                // Only calculate overtime if worked at least 8 hours (480 minutes)
                if (totalWorkedMinutes >= 480) 
                {
                    if (ActualCheckOut.Value > scheduledOutDateTime)
                    {
                        OvertimeMinutes = (int)(ActualCheckOut.Value - scheduledOutDateTime).TotalMinutes;
                    }
                }
            }
        }
    }
}
