using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IAttendanceAdjustmentRepository
{
    Task<AttendanceAdjustment?> GetByEmployeeAndDateAsync(
        EmployeeId employeeId,
        DateTime date,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceAdjustment>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        BranchId? branchId = null,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ajustes de RH que fuerzan el horario (impide borrarlo).</summary>
    Task<int> CountByShiftOverrideAsync(ShiftId shiftId, CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken = default);
    void Update(AttendanceAdjustment adjustment);
    void Remove(AttendanceAdjustment adjustment);

    /// <summary>Agrega una entrada de auditoría de la decisión (F05-06).</summary>
    Task AddHistoryAsync(AttendanceAdjustmentHistory entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceAdjustmentHistory>> GetHistoryAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default);
}
