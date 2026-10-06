using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Queries.GetRosterEmployees;

public record RosterEmployeeDto(
    string EmployeeId,
    string FullName,
    string? DepartmentName,
    string? PositionName,
    string? BranchName,
    Guid BranchId,
    string SchemeName,
    DateTime? StartDate,
    DateTime? EndDate,
    int TotalAssignments,
    string? TodayStatus,
    bool HasRoster);

public record GetRosterEmployeesQuery(
    Guid? BranchId = null,
    string? SearchTerm = null) : IRequest<List<RosterEmployeeDto>>;

public class GetRosterEmployeesQueryHandler : IRequestHandler<GetRosterEmployeesQuery, List<RosterEmployeeDto>>
{
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;

    public GetRosterEmployeesQueryHandler(
        IShiftRosterRepository rosterRepository,
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository)
    {
        _rosterRepository = rosterRepository;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _shiftRepository = shiftRepository;
    }

    public async Task<List<RosterEmployeeDto>> Handle(GetRosterEmployeesQuery request, CancellationToken cancellationToken)
    {
        var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
        var allRosters = await _rosterRepository.GetAllAsync(cancellationToken);
        var shifts = (await _shiftRepository.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);
        var branches = (await _branchRepository.GetAllAsync(cancellationToken)).ToDictionary(b => b.Id, b => b.Name);
        var departments = (await _departmentRepository.GetAllAsync(cancellationToken)).ToDictionary(d => d.Id, d => d.Name);
        var positions = (await _positionRepository.GetAllAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Name);

        var rostersByEmployee = allRosters
            .GroupBy(r => r.EmployeeId.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<RosterEmployeeDto>();
        var today = DateTime.Today;

        foreach (var emp in allEmployees)
        {
            if (request.BranchId.HasValue && emp.BranchId.Value != request.BranchId.Value)
                continue;

            var empIdStr = emp.Id.Value;
            var hasRosterEntries = rostersByEmployee.TryGetValue(empIdStr, out var empRosters) && empRosters.Any();
            var isRotativoType = emp.ShiftType == ShiftType.Rotativo;

            // Include if they have ShiftType.Rotativo OR have roster entries
            if (!hasRosterEntries && !isRotativoType)
                continue;

            string schemeName = "Rotativo";
            DateTime? startDate = null;
            DateTime? endDate = null;
            int totalAssignments = 0;
            string todayStatus = "Sin proyección";

            if (hasRosterEntries && empRosters != null)
            {
                startDate = empRosters.Min(r => r.Date);
                endDate = empRosters.Max(r => r.Date);
                totalAssignments = empRosters.Count;

                // Detect scheme name from notes
                var sampleNote = empRosters.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Notes))?.Notes;
                if (!string.IsNullOrWhiteSpace(sampleNote))
                {
                    if (sampleNote.Contains("2x8", StringComparison.OrdinalIgnoreCase))
                        schemeName = "Rotativo 2x8";
                    else if (sampleNote.Contains("3x8", StringComparison.OrdinalIgnoreCase))
                        schemeName = "Rotativo 3x8";
                    else if (sampleNote.Contains("4x3", StringComparison.OrdinalIgnoreCase))
                        schemeName = "Esquema 4x3";
                    else if (sampleNote.Contains("24x48", StringComparison.OrdinalIgnoreCase))
                        schemeName = "Esquema 24x48";
                    else
                        schemeName = sampleNote.Split('(')[0].Trim();
                }

                // Check today status
                var todayEntry = empRosters.FirstOrDefault(r => r.Date.Date == today);
                if (todayEntry != null)
                {
                    if (todayEntry.IsRestDay)
                    {
                        todayStatus = "Día de Descanso";
                    }
                    else if (todayEntry.ShiftId != null && shifts.TryGetValue(todayEntry.ShiftId, out var shift))
                    {
                        todayStatus = $"{shift.Name} ({shift.StartTime:hh\\:mm} - {shift.EndTime:hh\\:mm})";
                    }
                    else
                    {
                        todayStatus = "Turno Asignado";
                    }
                }
                else
                {
                    todayStatus = "Sin asignación hoy";
                }
            }
            else
            {
                schemeName = "Rotativo (Sin proyección)";
            }

            var fullName = emp.GetFullName();
            if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                var search = request.SearchTerm.Trim();
                if (!fullName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !empIdStr.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !schemeName.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var branchName = branches.TryGetValue(emp.BranchId, out var bName) ? bName : string.Empty;
            var departmentName = departments.TryGetValue(emp.DepartmentId, out var dName) ? dName : string.Empty;
            var positionName = positions.TryGetValue(emp.PositionId, out var pName) ? pName : string.Empty;

            result.Add(new RosterEmployeeDto(
                empIdStr,
                fullName,
                departmentName,
                positionName,
                branchName,
                emp.BranchId.Value,
                schemeName,
                startDate,
                endDate,
                totalAssignments,
                todayStatus,
                hasRosterEntries));
        }

        return result.OrderBy(r => r.FullName).ToList();
    }
}
