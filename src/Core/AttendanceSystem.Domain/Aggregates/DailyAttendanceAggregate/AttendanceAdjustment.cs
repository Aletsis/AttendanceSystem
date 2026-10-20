namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Representa una decisión o ajuste manual explícito realizado por un supervisor o Recursos Humanos
/// sobre un día laboral específico de un empleado, independiente del resultado derivado del cálculo.
/// </summary>
public enum AttendancePosition
{
    CheckIn = 1,
    Block1CheckOut = 2,
    Block2CheckIn = 3,
    CheckOut = 4
}

public enum PositionOverrideKind
{
    /// <summary>Sin decisión: la posición la decide el cálculo automático.</summary>
    None = 0,

    /// <summary>Asignación forzada del marcaje indicado.</summary>
    Assigned = 1,

    /// <summary>La posición permanece vacía; el marcaje indicado (si lo hay) no se reasigna automáticamente.</summary>
    ForcedEmpty = 2
}

public sealed class AttendanceAdjustment : Entity<Guid>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime Date { get; private set; }

    // Overrides de Turno y Descanso
    public bool? IsRestDayOverride { get; private set; }
    public ShiftId? ShiftIdOverride { get; private set; }

    // Asignaciones manuales de marcajes biométricos
    // Override por posición (F05-01): sin decisión, asignación forzada o vacío forzado.
    public PositionOverrideKind CheckInOverride { get; private set; }
    public PositionOverrideKind Block1CheckOutOverride { get; private set; }
    public PositionOverrideKind Block2CheckInOverride { get; private set; }
    public PositionOverrideKind CheckOutOverride { get; private set; }

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

    /// <summary>Se incrementa en cada cambio; permite rechazar decisiones tomadas sobre una versión anterior.</summary>
    public int Revision { get; private set; }

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

    public PositionOverrideKind OverrideKind(AttendancePosition position) => position switch
    {
        AttendancePosition.CheckIn => CheckInOverride,
        AttendancePosition.Block1CheckOut => Block1CheckOutOverride,
        AttendancePosition.Block2CheckIn => Block2CheckInOverride,
        _ => CheckOutOverride
    };

    public AttendanceRecordId? OverrideRecord(AttendancePosition position) => position switch
    {
        AttendancePosition.CheckIn => CheckInRecordId,
        AttendancePosition.Block1CheckOut => Block1CheckOutRecordId,
        AttendancePosition.Block2CheckIn => Block2CheckInRecordId,
        _ => CheckOutRecordId
    };

    public DateTime? OverrideTime(AttendancePosition position) => position switch
    {
        AttendancePosition.CheckIn => CheckInTimeOverride,
        AttendancePosition.Block1CheckOut => Block1CheckOutTimeOverride,
        AttendancePosition.Block2CheckIn => Block2CheckInTimeOverride,
        _ => CheckOutTimeOverride
    };

    /// <summary>
    /// F05-01 / F05-05: asigna un marcaje a una posición. Un marcaje ocupa a lo sumo una posición de la
    /// decisión: si estaba en otra (asignado o excluido), se retira de ella.
    /// </summary>
    public void AssignPosition(AttendancePosition position, AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null)
    {
        foreach (var other in AllPositions.Where(p => p != position && OverrideRecord(p) == recordId))
        {
            SetPosition(other, PositionOverrideKind.None, null, null);
        }
        SetPosition(position, PositionOverrideKind.Assigned, recordId, checkTime);
        Touch(updatedBy, reason);
    }

    /// <summary>
    /// F05-01: la posición debe permanecer vacía. Si se indica el marcaje retirado, queda excluido de la
    /// selección automática (C-04): el recálculo no vuelve a asignarlo en ninguna jornada.
    /// </summary>
    public void ForceEmptyPosition(AttendancePosition position, AttendanceRecordId? removedRecordId, string updatedBy, string? reason = null)
    {
        if (removedRecordId != null)
        {
            foreach (var other in AllPositions.Where(p => p != position && OverrideRecord(p) == removedRecordId))
            {
                SetPosition(other, PositionOverrideKind.None, null, null);
            }
        }
        SetPosition(position, PositionOverrideKind.ForcedEmpty, removedRecordId, null);
        Touch(updatedBy, reason);
    }

    /// <summary>Retira la decisión sobre una posición: el cálculo automático vuelve a decidirla.</summary>
    public void ClearPosition(AttendancePosition position, string updatedBy, string? reason = null)
    {
        SetPosition(position, PositionOverrideKind.None, null, null);
        Touch(updatedBy, reason);
    }

    /// <summary>Retira toda decisión sobre un marcaje (asignación o exclusión). Devuelve si había alguna.</summary>
    public bool RestoreAutomatic(AttendanceRecordId recordId, string updatedBy, string? reason = null)
    {
        var positions = AllPositions.Where(p => OverrideRecord(p) == recordId).ToList();
        foreach (var position in positions)
        {
            SetPosition(position, PositionOverrideKind.None, null, null);
        }
        if (positions.Count > 0) Touch(updatedBy, reason);
        return positions.Count > 0;
    }

    public void SetCheckIn(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null) =>
        AssignPosition(AttendancePosition.CheckIn, recordId, checkTime, updatedBy, reason);

    public void SetCheckOut(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null) =>
        AssignPosition(AttendancePosition.CheckOut, recordId, checkTime, updatedBy, reason);

    public void SetBlock1CheckOut(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null) =>
        AssignPosition(AttendancePosition.Block1CheckOut, recordId, checkTime, updatedBy, reason);

    public void SetBlock2CheckIn(AttendanceRecordId recordId, DateTime checkTime, string updatedBy, string? reason = null) =>
        AssignPosition(AttendancePosition.Block2CheckIn, recordId, checkTime, updatedBy, reason);

    /// <summary>Marcajes con decisión manual en esta jornada: asignados y excluidos (reservados frente al cálculo).</summary>
    public IEnumerable<AttendanceRecordId> DecidedRecordIds() =>
        AllPositions
            .Where(p => OverrideKind(p) != PositionOverrideKind.None && OverrideRecord(p) != null)
            .Select(p => OverrideRecord(p)!);

    /// <summary>Marcajes excluidos por vacío forzado.</summary>
    public IEnumerable<AttendanceRecordId> ExcludedRecordIds() =>
        AllPositions
            .Where(p => OverrideKind(p) == PositionOverrideKind.ForcedEmpty && OverrideRecord(p) != null)
            .Select(p => OverrideRecord(p)!);

    public static readonly IReadOnlyList<AttendancePosition> AllPositions = new[]
    {
        AttendancePosition.CheckIn, AttendancePosition.Block1CheckOut, AttendancePosition.Block2CheckIn, AttendancePosition.CheckOut
    };

    private void SetPosition(AttendancePosition position, PositionOverrideKind kind, AttendanceRecordId? recordId, DateTime? time)
    {
        switch (position)
        {
            case AttendancePosition.CheckIn:
                CheckInOverride = kind; CheckInRecordId = recordId; CheckInTimeOverride = time; break;
            case AttendancePosition.Block1CheckOut:
                Block1CheckOutOverride = kind; Block1CheckOutRecordId = recordId; Block1CheckOutTimeOverride = time; break;
            case AttendancePosition.Block2CheckIn:
                Block2CheckInOverride = kind; Block2CheckInRecordId = recordId; Block2CheckInTimeOverride = time; break;
            default:
                CheckOutOverride = kind; CheckOutRecordId = recordId; CheckOutTimeOverride = time; break;
        }
    }

    /// <summary>Resumen legible y estable de la decisión, para la auditoría antes/después (F05-06).</summary>
    public string Describe()
    {
        string Position(AttendancePosition p) => OverrideKind(p) switch
        {
            PositionOverrideKind.Assigned => $"{p}=asignado:{OverrideRecord(p)}",
            PositionOverrideKind.ForcedEmpty => $"{p}=vacío{(OverrideRecord(p) != null ? $"(excluye {OverrideRecord(p)})" : string.Empty)}",
            _ => $"{p}=auto"
        };

        return string.Join("; ", AllPositions.Select(Position).Concat(new[]
        {
            $"descanso={(IsRestDayOverride?.ToString() ?? "auto")}",
            $"turno={(ShiftIdOverride?.Value.ToString() ?? "auto")}",
            $"salidaTemporal={(TemporaryExitStatus?.ToString() ?? "auto")}",
            $"extrasAutorizadas={(OvertimeAuthorizedOverride?.ToString() ?? "auto")}",
            $"extrasAntesEntrada={(CalculateOvertimeBeforeEntryOverride?.ToString() ?? "auto")}"
        }));
    }

    public void SetTemporaryExitClassification(TemporaryExitStatus status, string note, string updatedBy)
    {
        TemporaryExitStatus = status;
        TemporaryExitNote = note;
        Touch(updatedBy, $"Clasificación de salida temporal a {status}");
    }

    public void ClearTemporaryExitClassification(string updatedBy, string? reason = null)
    {
        TemporaryExitStatus = null;
        TemporaryExitNote = null;
        Touch(updatedBy, reason);
    }

    public void ClearOvertimeConfiguration(string updatedBy, string? reason = null)
    {
        OvertimeAuthorizedOverride = null;
        CalculateOvertimeBeforeEntryOverride = null;
        Touch(updatedBy, reason);
    }

    public void SetOvertimeConfiguration(bool overtimeAuthorized, bool calculateOvertimeBeforeEntry, string updatedBy)
    {
        OvertimeAuthorizedOverride = overtimeAuthorized;
        CalculateOvertimeBeforeEntryOverride = calculateOvertimeBeforeEntry;
        Touch(updatedBy, "Actualización de configuración de horas extras");
    }

    private void Touch(string updatedBy, string? reason)
    {
        Revision++;
        UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "Sistema" : updatedBy;
        UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = reason;
        }
    }
}
