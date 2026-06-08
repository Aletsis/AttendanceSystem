using System;
using System.Collections.Generic;

namespace AttendanceSystem.Application.DTOs.Import;

public sealed record ImportResult<T>
{
    public List<T> ValidEntries { get; init; } = new();
    public List<string> Errors { get; init; } = new();

    public bool IsSuccess => Errors.Count == 0;
    public List<T> Data => ValidEntries;
    public string ErrorMessage => string.Join(", ", Errors);
}

public sealed record ImportedLogEntryDto(string EmployeeId, DateTime DateTime, string Type);

public sealed record ImportBranchDto(string Code, string Name, string Address, bool IsExternal = false, string? ExternalHost = null);

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
