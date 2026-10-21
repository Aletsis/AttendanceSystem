using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// F07-04 / C01: solicitudes de consolidación ya registradas en el ámbito actual (una petición o un lote). La
/// ingestión registra las de todo el lote en una sentencia; el manejador del evento de cada marcaje sólo consulta
/// la base si su jornada no quedó cubierta (p. ej. cuando el outbox reintenta en otro ámbito).
/// </summary>
public interface IConsolidationRequestTracker
{
    void MarkRequested(EmployeeId employeeId, DateTime startDate, DateTime endDate);

    bool IsCovered(EmployeeId employeeId, DateTime startDate, DateTime endDate);
}

public sealed class ConsolidationRequestTracker : IConsolidationRequestTracker
{
    private readonly Dictionary<EmployeeId, List<(DateTime Start, DateTime End)>> _requested = new();

    public void MarkRequested(EmployeeId employeeId, DateTime startDate, DateTime endDate)
    {
        if (!_requested.TryGetValue(employeeId, out var ranges)) _requested[employeeId] = ranges = new();
        ranges.Add((startDate.Date, endDate.Date));
    }

    public bool IsCovered(EmployeeId employeeId, DateTime startDate, DateTime endDate) =>
        _requested.TryGetValue(employeeId, out var ranges) &&
        ranges.Any(r => r.Start <= startDate.Date && r.End >= endDate.Date);
}
