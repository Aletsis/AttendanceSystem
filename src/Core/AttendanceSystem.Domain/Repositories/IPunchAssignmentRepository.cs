using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IPunchAssignmentRepository
{
    /// <summary>
    /// Asignaciones vigentes de los marcajes indicados, sin importar la jornada propietaria.
    /// </summary>
    Task<IReadOnlyList<PunchAssignment>> GetByRecordIdsAsync(
        IReadOnlyCollection<AttendanceRecordId> recordIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asignaciones de las jornadas de un empleado dentro del rango de fechas laborales [inicio, fin].
    /// </summary>
    Task<IReadOnlyList<PunchAssignment>> GetByEmployeeAndWorkDateRangeAsync(
        EmployeeId employeeId,
        DateOnly startWorkDate,
        DateOnly endWorkDate,
        CancellationToken cancellationToken = default);

    Task<PunchAssignment?> GetByRecordIdAsync(
        AttendanceRecordId recordId,
        CancellationToken cancellationToken = default);

    void Add(PunchAssignment assignment);
    void Update(PunchAssignment assignment);
    void Remove(PunchAssignment assignment);
}
