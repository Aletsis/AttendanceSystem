namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// F07-02: petición con un rango de fechas que se valida antes de ejecutarse (DateRangeValidationBehavior), igual
/// para Blazor, WPF, trabajos programados y consumidores internos.
/// </summary>
public interface IDateRangeRequest
{
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    int MaxRangeDays { get; }
}
