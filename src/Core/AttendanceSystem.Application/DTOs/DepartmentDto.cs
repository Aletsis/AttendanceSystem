namespace AttendanceSystem.Application.DTOs;

public record DepartmentDto(Guid Id, string Name, string? Description, List<Guid>? PositionIds = null);
