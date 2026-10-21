using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Datos para calcular las jornadas [from, to] de un lote de empleados: jornadas y ajustes de [from−1, to+1],
/// rosters de [from, to], marcajes con fecha laboral en [from, to+1] y las asignaciones de esas jornadas y marcajes.
/// </summary>
public sealed record CalculationWindow(
    IReadOnlyList<DailyAttendance> Dailies,
    IReadOnlyList<ShiftRoster> Rosters,
    IReadOnlyList<AttendanceAdjustment> Adjustments,
    IReadOnlyList<AttendanceRecord> Records,
    IReadOnlyList<PunchAssignment> Assignments);

/// <summary>
/// F07-03 / F07-04: lecturas del recálculo filtradas en la base de datos y por lotes de empleados. Las entidades
/// devueltas (salvo los empleados) quedan seguidas por el contexto del recálculo.
/// </summary>
public interface IAttendanceCalculationReader
{
    /// <summary>
    /// Empleados con relación laboral en el rango, opcionalmente de una sucursal (por vigencia), uno solo o un conjunto.
    /// </summary>
    Task<IReadOnlyList<Employee>> GetEmployeesActiveDuringAsync(
        DateTime start, DateTime end, BranchId? branchId, EmployeeId? employeeId, IReadOnlyCollection<EmployeeId>? employeeIds, CancellationToken cancellationToken);

    Task<CalculationWindow> LoadWindowAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime from, DateTime to, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttendanceRecord>> GetRecordsByIdsAsync(IReadOnlyCollection<AttendanceRecordId> recordIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<DailyAttendance>> GetDailiesAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime start, DateTime end, CancellationToken cancellationToken);
}

/// <summary>
/// Lector sobre los repositorios de dominio (una consulta por empleado); se usa cuando el host no registra un
/// lector por lotes (pruebas, hosts sin PostgreSQL). Mismo resultado que el lector por lotes.
/// </summary>
public sealed class RepositoryCalculationReader : IAttendanceCalculationReader
{
    private readonly IEmployeeRepository _employees;
    private readonly IDailyAttendanceRepository _dailies;
    private readonly IShiftRosterRepository _rosters;
    private readonly IAttendanceAdjustmentRepository _adjustments;
    private readonly IAttendanceRepository _records;
    private readonly IPunchAssignmentRepository _assignments;

    public RepositoryCalculationReader(
        IEmployeeRepository employees,
        IDailyAttendanceRepository dailies,
        IShiftRosterRepository rosters,
        IAttendanceAdjustmentRepository adjustments,
        IAttendanceRepository records,
        IPunchAssignmentRepository assignments)
    {
        _employees = employees;
        _dailies = dailies;
        _rosters = rosters;
        _adjustments = adjustments;
        _records = records;
        _assignments = assignments;
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesActiveDuringAsync(
        DateTime start, DateTime end, BranchId? branchId, EmployeeId? employeeId, IReadOnlyCollection<EmployeeId>? employeeIds, CancellationToken cancellationToken)
    {
        IEnumerable<Employee> employees = (await _employees.GetAllAsync(cancellationToken)) ?? Array.Empty<Employee>();
        if (employeeId != null) employees = employees.Where(e => e.Id == employeeId);
        if (employeeIds != null)
        {
            var set = employeeIds.ToHashSet();
            employees = employees.Where(e => set.Contains(e.Id));
        }
        return employees
            .Where(e => e.IsActiveDuring(start, end) && (branchId == null || e.WasInBranchDuring(branchId, start, end)))
            .ToList();
    }

    public async Task<CalculationWindow> LoadWindowAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var claimFrom = from.AddDays(-1);
        var claimTo = to.AddDays(1);
        var dailies = new List<DailyAttendance>();
        var rosters = new List<ShiftRoster>();
        var adjustments = new List<AttendanceAdjustment>();
        var records = new List<AttendanceRecord>();
        var assignments = new List<PunchAssignment>();

        foreach (var id in employeeIds)
        {
            dailies.AddRange((await _dailies.GetByDateRangeAsync(claimFrom, claimTo, null, id, cancellationToken)) ?? new List<DailyAttendance>());
            rosters.AddRange((await _rosters.GetByDateRangeAsync(from, to, null, id, cancellationToken)) ?? Array.Empty<ShiftRoster>());
            adjustments.AddRange((await _adjustments.GetByDateRangeAsync(claimFrom, claimTo, null, id, cancellationToken)) ?? Array.Empty<AttendanceAdjustment>());
            var employeeRecords = (await _records.GetByDateRangeAsync(DateOnly.FromDateTime(from), DateOnly.FromDateTime(claimTo), id, cancellationToken)) ?? Array.Empty<AttendanceRecord>();
            records.AddRange(employeeRecords);
            assignments.AddRange((await _assignments.GetByEmployeeAndWorkDateRangeAsync(id, DateOnly.FromDateTime(claimFrom), DateOnly.FromDateTime(claimTo), cancellationToken)) ?? Array.Empty<PunchAssignment>());
            if (employeeRecords.Count > 0)
            {
                assignments.AddRange((await _assignments.GetByRecordIdsAsync(employeeRecords.Select(r => r.Id).ToList(), cancellationToken)) ?? Array.Empty<PunchAssignment>());
            }
        }

        return new CalculationWindow(dailies, rosters, adjustments, records, assignments);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetRecordsByIdsAsync(IReadOnlyCollection<AttendanceRecordId> recordIds, CancellationToken cancellationToken)
    {
        var result = new List<AttendanceRecord>();
        foreach (var id in recordIds)
        {
            var record = await _records.GetByIdAsync(id, cancellationToken);
            if (record != null) result.Add(record);
        }
        return result;
    }

    public async Task<IReadOnlyList<DailyAttendance>> GetDailiesAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var result = new List<DailyAttendance>();
        foreach (var id in employeeIds)
        {
            result.AddRange((await _dailies.GetByEmployeeAndDateRangeAsync(id, start, end, cancellationToken)) ?? new List<DailyAttendance>());
        }
        return result;
    }
}
