using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees; // Check if AttendanceReportViewDto is here or DTOs

namespace AttendanceSystem.Application.Abstractions;

public interface IReportExportService
{
    byte[] GenerateExcel(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo, bool detailed = false);
    byte[] GeneratePdf(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo);
    byte[] GenerateAttendanceCardsPdf(Dictionary<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>> groupedData, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo, bool includeOvertime = true);
    
    // Catalogs
    byte[] GenerateBranchesExcel(IEnumerable<BranchDto> branches);
    byte[] GenerateDepartmentsExcel(IEnumerable<DepartmentDto> departments);
    byte[] GenerateEmployeesExcel(IEnumerable<EmployeeDto> employees);
    byte[] GeneratePositionsExcel(IEnumerable<PositionDto> positions);

    // Advanced Reports
    byte[] GenerateAdvancedAbsenceExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch);
    byte[] GenerateWorkedRestDayExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool showBranch);
    byte[] GenerateLateArrivalExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch);
    byte[] GenerateOvertimeExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch, bool groupByDepartment = false);
    byte[] GenerateAttendanceLogsExcel(IEnumerable<AttendanceLogViewDto> logs);

    // Payroll Exports
    byte[] GenerateContpaqiNominaExport(IEnumerable<AttendanceReportViewDto> attendanceData);
    byte[] GenerateAspelNoiExport(IEnumerable<AttendanceReportViewDto> attendanceData);
}
