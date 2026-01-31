using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Features.Departments.Queries;

public interface IDepartmentQueries
{
    Task<IEnumerable<DepartmentDto>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetDepartmentByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
