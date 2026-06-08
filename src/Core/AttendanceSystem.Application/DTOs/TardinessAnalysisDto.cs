namespace AttendanceSystem.Application.DTOs;

public class TardinessAnalysisDto
{
    public double TardinessRate { get; set; }
    public int TotalTardies { get; set; }
    public int TotalPossibleWorkDays { get; set; }
    public int TotalTardinessMinutes { get; set; }
    
    public List<DayTardinessDto> TardinessByDayOfWeek { get; set; } = new();
    public List<DepartmentTardinessDto> TardinessByDepartment { get; set; } = new();
    public List<BranchTardinessDto> TardinessByBranch { get; set; } = new();
    public List<BranchDepartmentTardinessDto> TardinessByBranchDepartment { get; set; } = new();
    public List<EmployeeTardinessDto> TopTardyEmployees { get; set; } = new();
}

public record DayTardinessDto(string DayName, int TardyCount, double Rate, int TotalMinutes);
public record DepartmentTardinessDto(string DepartmentName, int TardyCount, int TotalEmployees, double Rate, int TotalMinutes);
public record EmployeeTardinessDto(string EmployeeName, string Department, int TardyCount, int TotalMinutes);
public record BranchTardinessDto(string BranchName, int TardyCount, int TotalEmployees, double Rate, int TotalMinutes);
public record BranchDepartmentTardinessDto(string BranchName, string DepartmentName, int TardyCount, double Rate, int TotalMinutes);
