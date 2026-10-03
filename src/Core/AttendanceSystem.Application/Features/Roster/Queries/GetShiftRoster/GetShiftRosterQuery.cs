using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Queries.GetShiftRoster;

public record GetShiftRosterQuery(
    DateTime StartDate,
    DateTime EndDate,
    Guid? BranchId = null,
    string? EmployeeId = null) : IRequest<List<ShiftRosterDto>>;

public class GetShiftRosterQueryHandler : IRequestHandler<GetShiftRosterQuery, List<ShiftRosterDto>>
{
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IShiftRepository _shiftRepository;

    public GetShiftRosterQueryHandler(
        IShiftRosterRepository rosterRepository,
        IEmployeeRepository employeeRepository,
        IShiftRepository shiftRepository)
    {
        _rosterRepository = rosterRepository;
        _employeeRepository = employeeRepository;
        _shiftRepository = shiftRepository;
    }

    public async Task<List<ShiftRosterDto>> Handle(GetShiftRosterQuery request, CancellationToken cancellationToken)
    {
        var branchId = request.BranchId.HasValue ? BranchId.From(request.BranchId.Value) : null;
        var empId = !string.IsNullOrWhiteSpace(request.EmployeeId) ? EmployeeId.From(request.EmployeeId) : null;

        var rosters = await _rosterRepository.GetByDateRangeAsync(
            request.StartDate,
            request.EndDate,
            branchId,
            empId,
            cancellationToken);

        var employees = (await _employeeRepository.GetAllAsync(cancellationToken))
            .ToDictionary(e => e.Id.Value);

        var shifts = (await _shiftRepository.GetAllAsync(cancellationToken))
            .ToDictionary(s => s.Id);

        var result = new List<ShiftRosterDto>();

        foreach (var r in rosters)
        {
            var empName = employees.TryGetValue(r.EmployeeId.Value, out var emp)
                ? $"{emp.FirstName} {emp.LastName}"
                : r.EmployeeId.Value;

            if (r.ShiftId != null && shifts.TryGetValue(r.ShiftId, out var shift))
            {
                result.Add(new ShiftRosterDto(
                    r.Id.Value,
                    r.EmployeeId.Value,
                    empName,
                    r.Date,
                    shift.Id.Value,
                    shift.Name,
                    shift.ShiftType,
                    shift.StartTime,
                    shift.EndTime,
                    r.IsRestDay,
                    r.Notes));
            }
            else
            {
                result.Add(new ShiftRosterDto(
                    r.Id.Value,
                    r.EmployeeId.Value,
                    empName,
                    r.Date,
                    null,
                    r.IsRestDay ? "Día de Descanso" : null,
                    null,
                    null,
                    null,
                    r.IsRestDay,
                    r.Notes));
            }
        }

        return result.OrderBy(r => r.Date).ThenBy(r => r.EmployeeName).ToList();
    }
}
