using System;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.DTOs;

public sealed record AttendanceReportViewDto
{
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string PositionName { get; init; } = string.Empty;
    public TimeSpan? ScheduledCheckIn { get; init; }
    public TimeSpan? ScheduledCheckOut { get; init; }
    /// <summary>Hora civil de la sucursal (no el instante UTC).</summary>
    public DateTime? ActualCheckIn { get; init; }
    public DateTime? ActualCheckOut { get; init; }
    public int LateMinutes { get; init; }
    public int EarlyDepartureMinutes { get; init; }
    public int OvertimeMinutes { get; init; }

    public bool IsAbsent { get; init; }
    public bool IsRestDay { get; init; }
    public bool WorkedOnRestDay { get; init; }
    public bool MissingCheckIn { get; init; }
    public bool MissingCheckOut { get; init; }
    /// <summary>Al menos un marcaje en la jornada (<c>DailyAttendance.HasPunches</c>).</summary>
    public bool HasPunches { get; init; }

    // --- Turnos Partidos / Doble Turno ---
    public ShiftType? ShiftType { get; init; }
    public TimeSpan? ScheduledBlock2CheckIn { get; init; }
    public TimeSpan? ScheduledBlock2CheckOut { get; init; }
    public DateTime? ActualBlock1CheckOut { get; init; }
    public DateTime? ActualBlock2CheckIn { get; init; }
    public bool MissingBlock1CheckOut { get; init; }
    public bool MissingBlock2CheckIn { get; init; }

    // --- Salidas Temporales Detectadas ---
    public bool HasTemporaryExits { get; init; }
    public int TemporaryExitMinutes { get; init; }
    /// <summary>Resumen: 0=Pending, 1=ApprovedPaid, 2=ApprovedUnpaid, 3=Dismissed (pendiente si alguna salida lo está).</summary>
    public int TemporaryExitStatus { get; init; }
    /// <summary>Caso 11: cada salida (incluidas las comidas) para clasificarla por separado.</summary>
    public IReadOnlyList<TemporaryExitDto> TemporaryExits { get; init; } = Array.Empty<TemporaryExitDto>();
    /// <summary>Notas de la jornada tal como las define <c>DailyAttendance.Notes</c>.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public string? AttendanceNote { get; init; }

    // --- Comida ---
    public int LunchMinutesTaken { get; init; }
    /// <summary>Excedente de comida sobre los minutos con goce: lo que se descuenta.</summary>
    public int LunchDeductibleMinutes { get; init; }

    public OvertimeCalculationMethod OvertimeCalculationMethod { get; init; }
    public int TotalWorkedMinutes { get; init; }
    public PunchTrackingMode PunchTrackingMode { get; init; } = PunchTrackingMode.SingleInterval;
    public string? IntervalsData { get; init; }

    /// <summary>
    /// Extras efectivas tal como las guardó el motor (ya redondeadas una vez y con topes). F06-09: no se vuelven a
    /// redondear con el método actual del empleado, que reinterpretaba la historia y redondeaba dos veces.
    /// </summary>
    public int RoundedOvertimeMinutes => Math.Max(0, OvertimeMinutes);
}

/// <summary>Una salida de la jornada (hora civil) con su clasificación (caso 11).</summary>
public sealed record TemporaryExitDto(Guid ExitRecordId, DateTime ExitTime, DateTime ReturnTime, int Minutes, TemporaryExitStatus Status, bool IsAutomaticLunch);
