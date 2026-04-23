namespace AttendanceSystem.Application.DTOs;

public record BranchDto(Guid Id, string Code, string Name, string? Address, bool IsExternal, string? ExternalHost);
