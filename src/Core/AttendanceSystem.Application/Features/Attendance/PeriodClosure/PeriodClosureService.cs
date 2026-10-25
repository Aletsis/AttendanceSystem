using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;

namespace AttendanceSystem.Application.Features.Attendance.PeriodClosure;

/// <summary>Un periodo de asistencia [inicio, fin].</summary>
public readonly record struct AttendancePeriod(DateTime Start, DateTime End)
{
    public bool Contains(DateTime date) => date.Date >= Start && date.Date <= End;

    public string Describe() => $"del {Start:dd/MM/yyyy} al {End:dd/MM/yyyy}";
}

/// <summary>Periodos según la configuración (el mismo cálculo que los topes de extras por periodo).</summary>
public static class AttendancePeriods
{
    public static AttendancePeriod Containing(SystemConfiguration config, DateTime date)
    {
        var (start, end) = Domain.Services.WorkPeriodCalculator.Containing(date, config);
        return new AttendancePeriod(start, end);
    }

    public static AttendancePeriod Previous(SystemConfiguration config, AttendancePeriod period) => Containing(config, period.Start.AddDays(-1));

    public static AttendancePeriod Next(SystemConfiguration config, AttendancePeriod period) => Containing(config, period.End.AddDays(1));
}

/// <summary>
/// Caso 17: qué días están cerrados. Un día de un periodo cerrado no se recalcula ni acepta ajustes; los cambios con
/// fecha pasada empiezan a recalcular en el primer día abierto.
/// </summary>
public sealed class PeriodClosureService
{
    public const int AutomaticCloseAfterDays = 3;
    public const string ClosedDayMessage = "El periodo {0} está cerrado: no se puede modificar la asistencia de ese día.";

    private readonly IAttendancePeriodClosureRepository _closures;

    public PeriodClosureService(IAttendancePeriodClosureRepository closures) => _closures = closures;

    /// <summary>Cierres vigentes (se consultan cada vez: un cierre o una reapertura aplica de inmediato).</summary>
    public async Task<IReadOnlyList<AttendancePeriodClosure>> ActiveAsync(CancellationToken cancellationToken) =>
        (await _closures.GetAllAsync(cancellationToken)).Where(c => c.IsActive).OrderBy(c => c.StartDate).ToList();

    /// <summary>Cierre vigente que incluye el día, o null si está abierto.</summary>
    public async Task<AttendancePeriodClosure?> ClosureOfAsync(DateTime date, CancellationToken cancellationToken) =>
        (await ActiveAsync(cancellationToken)).FirstOrDefault(c => c.Contains(date));

    /// <summary>Error para un ajuste sobre un día cerrado; null si el día está abierto.</summary>
    public async Task<string?> DenyAsync(DateTime date, CancellationToken cancellationToken) =>
        await ClosureOfAsync(date, cancellationToken) is { } closure
            ? string.Format(ClosedDayMessage, $"del {closure.StartDate:dd/MM/yyyy} al {closure.EndDate:dd/MM/yyyy}")
            : null;

    /// <summary>Recorta [desde, hasta] a sus días abiertos (primero y último abiertos); null si todo está cerrado.</summary>
    public async Task<(DateTime From, DateTime To)?> OpenRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var closures = await ActiveAsync(cancellationToken);
        var start = from.Date;
        while (start <= to.Date && closures.FirstOrDefault(c => c.Contains(start)) is { } c) start = c.EndDate.AddDays(1);
        var end = to.Date;
        while (end >= start && closures.FirstOrDefault(c => c.Contains(end)) is { } c) end = c.StartDate.AddDays(-1);
        return start <= end ? (start, end) : null;
    }

    /// <summary>Días cerrados entre [desde, hasta] para el cálculo (se saltan).</summary>
    public async Task<Func<DateTime, bool>> ClosedDaysAsync(CancellationToken cancellationToken)
    {
        var closures = await ActiveAsync(cancellationToken);
        return date => closures.Any(c => c.Contains(date));
    }
}
