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
        if (shift != null)
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

            // Rule: On rest days, overtime depends on assigned schedule if available
            if (ActualCheckIn.HasValue && ActualCheckOut.HasValue)
            {
                var totalMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;

                if (ScheduledCheckIn.HasValue && ScheduledCheckOut.HasValue)
                {
                     var schedIn = Date.Add(ScheduledCheckIn.Value);
                     var schedOut = Date.Add(ScheduledCheckOut.Value);
                     if (ScheduledCheckOut < ScheduledCheckIn)
                     {
                         schedOut = schedOut.AddDays(1);
                     }
                     var schedMinutes = (schedOut - schedIn).TotalMinutes;
                     
                     // Overtime = Worked - Scheduled
                     OvertimeMinutes = Math.Max(0, (int)(totalMinutes - schedMinutes));
                }
                else
                {
                    // Fallback: Default to 8 hours (480 min) deduction if strictly no schedule found
                    OvertimeMinutes = Math.Max(0, (int)totalMinutes - 480);
                }
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
                // If no schedule is known, we can't strictly compare.
                // Assuming default 8 hours? Or keeping existing logic?
                // Existing logic assumed > 480 check.
                // If we want "Worked - Scheduled" and Scheduled is unknown, this is ambiguous.
                // I will keep the existing fallback logic of > 480 for safety, 
                // or debatably all of it if Scheduled is considered 0? 
                // Context: "based on assigned schedules". If no schedule assigned, this block hits.
                // The previous code deducted 480. I'll stick to that 8h benchmark for "unknown schedule".
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
            // Rule: Overtime = Time from Scheduled CheckIn to Actual CheckOut - Scheduled Hours
            // Only count overtime if the employee worked the full scheduled hours
            if (ActualCheckIn.HasValue)
            {
                // Calculate actual worked minutes (from actual check-in to actual check-out)
                var totalWorkedMinutes = (ActualCheckOut.Value - ActualCheckIn.Value).TotalMinutes;
                
                // Calculate scheduled work duration
                var scheduledMinutes = (scheduledOutDateTime - scheduledInDateTime).TotalMinutes;

                // Only calculate overtime if employee worked at least the scheduled hours
                if (totalWorkedMinutes >= scheduledMinutes)
                {
                    // Calculate time from scheduled check-in to actual check-out
                    var timeFromScheduledStart = (ActualCheckOut.Value - scheduledInDateTime).TotalMinutes;
                    
                    // Overtime = Time from scheduled start to actual checkout - Scheduled duration
                    var overtime = timeFromScheduledStart - scheduledMinutes;
                    if (overtime > 0)
                    {
                        OvertimeMinutes = (int)overtime;
                    }
                }
            }
        }
    }
}
