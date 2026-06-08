namespace AttendanceSystem.Application.DTOs;

public record AbsenteeismAnalysisDto
{
    public double AbsenteeismRate { get; init; }
    public List<DayAbsenteeismDto> AbsencesByDayOfWeek { get; init; } = new();
    public List<DepartmentAbsenteeismDto> AbsencesByDepartment { get; init; } = new();
    public List<BranchAbsenteeismDto> AbsencesByBranch { get; init; } = new();
    public List<BranchDepartmentAbsenteeismDto> AbsencesByBranchDepartment { get; init; } = new();
    public List<EmployeeAbsenteeismDto> TopAbsentEmployees { get; init; } = new();
    public int TotalAbsences { get; init; }
    public int TotalPossibleWorkDays { get; init; }
}

public record DayAbsenteeismDto(string DayName, int AbsenceCount, double Percentage);
public record DepartmentAbsenteeismDto(string DepartmentName, int AbsenceCount, int TotalEmployees, double Rate);
public record BranchAbsenteeismDto(string BranchName, int AbsenceCount, int TotalEmployees, double Rate);
public record BranchDepartmentAbsenteeismDto(string BranchName, string DepartmentName, int AbsenceCount, double Rate);
public record EmployeeAbsenteeismDto(string EmployeeName, string Department, int AbsenceCount);
