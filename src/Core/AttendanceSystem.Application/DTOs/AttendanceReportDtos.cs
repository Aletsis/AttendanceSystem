using System;
using AttendanceSystem.Domain.Enumerations;

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
    public int OvertimeMinutes { get; init; }

    public bool IsAbsent { get; init; }
    public bool IsRestDay { get; init; }
    public bool WorkedOnRestDay { get; init; }
    public bool MissingCheckIn { get; init; }
    public bool MissingCheckOut { get; init; }

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
    /// <summary>0=Pending, 1=ApprovedPaid, 2=ApprovedUnpaid, 3=Dismissed</summary>
    public int TemporaryExitStatus { get; init; }
    public string? AttendanceNote { get; init; }

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
