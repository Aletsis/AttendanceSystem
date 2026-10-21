using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class PunchAssignmentRepository : IPunchAssignmentRepository
{
    private readonly AttendanceDbContext _context;

    public PunchAssignmentRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PunchAssignment>> GetByRecordIdsAsync(
        IReadOnlyCollection<AttendanceRecordId> recordIds,
        CancellationToken cancellationToken = default)
    {
        if (recordIds.Count == 0) return new List<PunchAssignment>();

        var ids = recordIds.Distinct().ToList();
        return await _context.PunchAssignments
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PunchAssignment>> GetByEmployeeAndWorkDateRangeAsync(
        EmployeeId employeeId,
        DateOnly startWorkDate,
        DateOnly endWorkDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.PunchAssignments
            .Where(x => x.EmployeeId == employeeId && x.WorkDate >= startWorkDate && x.WorkDate <= endWorkDate)
            .OrderBy(x => x.WorkDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<PunchAssignment?> GetByRecordIdAsync(
        AttendanceRecordId recordId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PunchAssignments
            .FirstOrDefaultAsync(x => x.Id == recordId, cancellationToken);
    }

    public void Add(PunchAssignment assignment)
    {
        _context.PunchAssignments.Add(assignment);
    }

    public void Update(PunchAssignment assignment)
    {
        // F07-07: sólo se adjuntan entidades sueltas; las seguidas escriben únicamente sus cambios reales.
        if (_context.Entry(assignment).State != EntityState.Detached) return;
        _context.PunchAssignments.Update(assignment);
    }

    public void Remove(PunchAssignment assignment)
    {
        _context.PunchAssignments.Remove(assignment);
    }
}
