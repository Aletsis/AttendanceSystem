using System;
using System.Collections.Generic;

namespace AttendanceSystem.Application.DTOs.Import;

public sealed record ImportResult<T>
{
    public List<T> ValidEntries { get; init; } = new();
    public List<string> Errors { get; init; } = new();
}

public sealed record ImportedLogEntryDto(string EmployeeId, DateTime DateTime, string Type);

public sealed record ImportBranchDto(string Name, string Description, string Address);

public sealed record ImportDepartmentDto(string Name, string Description);

public sealed record ImportPositionDto(string Name, string Description, decimal BaseSalary);

public sealed record ImportEmployeeDto(
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    string BranchName,
    string DepartmentName,
    string PositionName,
    string Gender,
    DateTime HireDate
);
