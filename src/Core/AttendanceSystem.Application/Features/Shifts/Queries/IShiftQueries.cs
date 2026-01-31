using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Features.Shifts.Queries;

public interface IShiftQueries
{
    Task<IEnumerable<ShiftDto>> GetAllShiftsAsync(CancellationToken cancellationToken = default);
}
