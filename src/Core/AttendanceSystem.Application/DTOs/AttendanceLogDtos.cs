using System;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.DTOs;

public sealed record AttendanceLogViewDto
{
    public Guid Id { get; init; }
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    /// <summary>Hora civil del marcaje en la zona de su sucursal (no el instante UTC).</summary>
    public DateTime CheckTime { get; init; }
    public string EntryType { get; init; } = "No Válida";
    public string VerifyMethod { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? AssignedDate { get; init; }
    /// <summary>Indica si la jornada correspondiente al marcaje es de horario partido / doble bloque (ShiftType.Partido).</summary>
    public bool IsSplitShift { get; init; }

    /// <summary>Caso 11: Información de salidas intermedias y comida.</summary>
    public bool IsTemporaryExit { get; init; }
    public bool IsTemporaryExitReturn { get; init; }
    public TemporaryExitStatus? TemporaryExitStatus { get; init; }
    public int? TemporaryExitMinutes { get; init; }
    public DateTime? TemporaryExitReturnTime { get; init; }
    public bool IsAutomaticLunch { get; init; }
    public string? TemporaryExitNote { get; init; }
    public int? DayRevision { get; init; }

    /// <summary>Caso 14: marcaje anulado (no cuenta en el cálculo), con quién, por qué y si lo reemplaza uno corregido.</summary>
    public bool IsVoided { get; init; }
    public string? VoidedBy { get; init; }
    public string? VoidReason { get; init; }
    public bool WasCorrected { get; init; }
    /// <summary>Caso 14: este marcaje es la corrección de otro.</summary>
    public bool IsCorrection { get; init; }
    public bool IsManual { get; init; }
    public string? RegisteredBy { get; init; }
    public string? ManualReason { get; init; }
}
