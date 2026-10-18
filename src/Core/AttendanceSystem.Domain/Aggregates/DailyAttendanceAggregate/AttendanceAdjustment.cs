namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Representa una decisión o ajuste manual explícito realizado por un supervisor o Recursos Humanos
/// sobre un día laboral específico de un empleado, independiente del resultado derivado del cálculo.
/// </summary>
public sealed class AttendanceAdjustment : Entity<Guid>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime Date { get; private set; }

    // Overrides de Turno y Descanso
    public bool? IsRestDayOverride { get; private set; }
    public ShiftId? ShiftIdOverride { get; private set; }

    // Asignaciones manuales de marcajes biométricos
    public AttendanceRecordId? CheckInRecordId { get; private set; }
    public DateTime? CheckInTimeOverride { get; private set; }
    public AttendanceRecordId? CheckOutRecordId { get; private set; }
    public DateTime? CheckOutTimeOverride { get; private set; }
    public AttendanceRecordId? Block1CheckOutRecordId { get; private set; }
    public DateTime? Block1CheckOutTimeOverride { get; private set; }
    public AttendanceRecordId? Block2CheckInRecordId { get; private set; }
    public DateTime? Block2CheckInTimeOverride { get; private set; }

    // Clasificación manual de salidas temporales e incidencias
    public TemporaryExitStatus? TemporaryExitStatus { get; private set; }
    public string? TemporaryExitNote { get; private set; }

    // Overrides de configuración de horas extras
    public bool? OvertimeAuthorizedOverride { get; private set; }
    public bool? CalculateOvertimeBeforeEntryOverride { get; private set; }

    // Auditoría
    public string? Reason { get; private set; }
    public string CreatedBy { get; private set; } = "Sistema";
    public DateTime CreatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private AttendanceAdjustment() { } // Para EF Core

    public static AttendanceAdjustment Create(
        EmployeeId employeeId,
        DateTime date,
        string createdBy,
        string? reason = null,
        DateTime? createdAt = null)
    {
        return new AttendanceAdjustment
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Date = date.Date,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "Sistema" : createdBy,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Reason = reason
        };
    }

    public void SetRestDayOverride(bool isRestDay, string updatedBy, string? reason = null)
    {
        IsRestDayOverride = isRestDay;
        Touch(updatedBy, reason);
    }

    public void ClearRestDayOverride(string updatedBy, string? reason = null)
    {
        IsRestDayOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetShiftOverride(ShiftId shiftId, string updatedBy, string? reason = null)
    {
        ShiftIdOverride = shiftId;
        Touch(updatedBy, reason);
    }

    public void ClearShiftOverride(string updatedBy, string? reason = null)
    {
        ShiftIdOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetCheckIn(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null)
    {
        CheckInRecordId = recordId;
        CheckInTimeOverride = checkTime;
        Touch(updatedBy, reason);
    }

    public void RemoveCheckIn(string updatedBy, string? reason = null)
    {
        CheckInRecordId = null;
        CheckInTimeOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetCheckOut(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null)
    {
        CheckOutRecordId = recordId;
        CheckOutTimeOverride = checkTime;
        Touch(updatedBy, reason);
    }

    public void RemoveCheckOut(string updatedBy, string? reason = null)
    {
        CheckOutRecordId = null;
        CheckOutTimeOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetBlock1CheckOut(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null)
    {
        Block1CheckOutRecordId = recordId;
        Block1CheckOutTimeOverride = checkTime;
        Touch(updatedBy, reason);
    }

    public void RemoveBlock1CheckOut(string updatedBy, string? reason = null)
    {
        Block1CheckOutRecordId = null;
        Block1CheckOutTimeOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetBlock2CheckIn(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null)
    {
        Block2CheckInRecordId = recordId;
        Block2CheckInTimeOverride = checkTime;
        Touch(updatedBy, reason);
    }

    public void RemoveBlock2CheckIn(string updatedBy, string? reason = null)
    {
        Block2CheckInRecordId = null;
        Block2CheckInTimeOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetTemporaryExitClassification(TemporaryExitStatus status, string note, string updatedBy)
    {
        TemporaryExitStatus = status;
        TemporaryExitNote = note;
        Touch(updatedBy, $"Clasificación de salida temporal a {status}");
    }

    public void SetOvertimeConfiguration(bool overtimeAuthorized, bool calculateOvertimeBeforeEntry, string updatedBy)
    {
        OvertimeAuthorizedOverride = overtimeAuthorized;
        CalculateOvertimeBeforeEntryOverride = calculateOvertimeBeforeEntry;
        Touch(updatedBy, "Actualización de configuración de horas extras");
    }

    private void Touch(string updatedBy, string? reason)
    {
        UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "Sistema" : updatedBy;
        UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = reason;
        }
    }
}
