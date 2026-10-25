using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

/// <summary>
/// F07-03 / F07-04: lecturas del recálculo filtradas en PostgreSQL por los empleados del lote (una consulta por
/// tipo de dato y lote, no por empleado). Mismas condiciones que los repositorios de dominio.
/// </summary>
public sealed class EfAttendanceCalculationReader : IAttendanceCalculationReader
{
    private readonly AttendanceDbContext _context;

    public EfAttendanceCalculationReader(AttendanceDbContext context) => _context = context;

    public async Task<IReadOnlyList<Employee>> GetEmployeesActiveDuringAsync(
        DateTime start, DateTime end, BranchId? branchId, EmployeeId? employeeId, IReadOnlyCollection<EmployeeId>? employeeIds, CancellationToken cancellationToken)
    {
        var from = start.Date;
        var to = end.Date;

        // Filtro amplio en SQL (vigencias de empleo y sucursal); el exacto en dominio. Sin huellas ni fotos: el
        // cálculo no las usa y los empleados no se modifican (sin seguimiento).
        var query = _context.Employees.AsNoTracking()
            .Where(e => e.EmploymentPeriods.Any(p => p.StartDate <= to && (p.EndDate == null || p.EndDate >= from)) ||
                        (!e.EmploymentPeriods.Any() && e.HireDate <= to && (e.TerminationDate == null || e.TerminationDate >= from)));
        if (employeeId != null) query = query.Where(e => e.Id == employeeId);
        if (employeeIds != null)
        {
            var ids = employeeIds.Distinct().ToList();
            query = query.Where(e => ids.Contains(e.Id));
        }
        if (branchId != null) query = query.Where(e => e.BranchId == branchId || e.ScheduleAssignments.Any(a => a.BranchId == branchId));

        var employees = await query.ToListAsync(cancellationToken);
        return employees
            .Where(e => e.IsEmployedDuring(start, end) && (branchId == null || e.WasInBranchDuring(branchId, start, end)))
            .ToList();
    }

    public async Task<CalculationWindow> LoadWindowAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        var claimFrom = from.Date.AddDays(-1);
        var claimTo = to.Date.AddDays(1);
        var fromDate = from.Date;
        var toDate = to.Date;

        var dailies = await _context.DailyAttendances
            .Where(x => ids.Contains(x.EmployeeId) && x.Date >= claimFrom && x.Date <= claimTo)
            .ToListAsync(cancellationToken);
        var rosters = await _context.Set<ShiftRoster>()
            .Where(r => ids.Contains(r.EmployeeId) && r.Date >= fromDate && r.Date <= toDate)
            .ToListAsync(cancellationToken);
        var adjustments = await _context.AttendanceAdjustments
            .Where(x => ids.Contains(x.EmployeeId) && x.Date >= claimFrom && x.Date <= claimTo)
            .ToListAsync(cancellationToken);

        // Igual que AttendanceRepository.GetByDateRangeAsync: fecha laboral, o instante UTC ampliado sin ella.
        var startWork = DateOnly.FromDateTime(fromDate);
        var endWork = DateOnly.FromDateTime(claimTo);
        var expandedStartUtc = startWork.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var expandedEndUtc = endWork.AddDays(1).ToDateTime(TimeOnly.MaxValue);
        var records = await _context.AttendanceRecords
            // Caso 14: los marcajes anulados no participan en el cálculo.
            .Where(x => ids.Contains(x.EmployeeId) && x.VoidedAt == null &&
                        ((x.WorkDate.HasValue && x.WorkDate.Value >= startWork && x.WorkDate.Value <= endWork) ||
                         (!x.WorkDate.HasValue && x.CheckTime >= expandedStartUtc && x.CheckTime <= expandedEndUtc)))
            .ToListAsync(cancellationToken);

        // Asignaciones de las jornadas vecinas y de los marcajes cargados (pueden pertenecer a jornadas fuera del rango).
        var claimStart = DateOnly.FromDateTime(claimFrom);
        var claimEnd = DateOnly.FromDateTime(claimTo);
        var recordIds = records.Select(r => r.Id).ToList();
        var assignments = await _context.PunchAssignments
            .Where(a => (ids.Contains(a.EmployeeId) && a.WorkDate >= claimStart && a.WorkDate <= claimEnd) || recordIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        return new CalculationWindow(dailies, rosters, adjustments, records, assignments);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetRecordsByIdsAsync(IReadOnlyCollection<AttendanceRecordId> recordIds, CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0) return Array.Empty<AttendanceRecord>();
        var ids = recordIds.Distinct().ToList();
        return await _context.AttendanceRecords.Where(r => ids.Contains(r.Id) && r.VoidedAt == null).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyAttendance>> GetDailiesAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        var from = start.Date;
        var to = end.Date;
        return await _context.DailyAttendances
            .Where(x => ids.Contains(x.EmployeeId) && x.Date >= from && x.Date <= to)
            .OrderBy(x => x.EmployeeId).ThenBy(x => x.Date)
            .ToListAsync(cancellationToken);
    }
}
