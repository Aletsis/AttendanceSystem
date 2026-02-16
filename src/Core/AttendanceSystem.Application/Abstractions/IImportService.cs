using System;
using System.IO;
using System.Threading.Tasks;
using AttendanceSystem.Application.DTOs.Import;

namespace AttendanceSystem.Application.Abstractions;

public interface IImportService
{
    Task<ImportResult<ImportedLogEntryDto>> ParseAttendanceLogsAsync(Stream stream, string fileName);
    Task<ImportResult<ImportBranchDto>> ParseBranchesAsync(Stream stream);
    Task<ImportResult<ImportDepartmentDto>> ParseDepartmentsAsync(Stream stream);
    Task<ImportResult<ImportPositionDto>> ParsePositionsAsync(Stream stream);
    Task<ImportResult<ImportEmployeeDto>> ParseEmployeesAsync(Stream stream);
}
