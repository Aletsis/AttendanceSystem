namespace AttendanceSystem.Application.DTOs;

public record PositionDto(Guid Id, string Name, string? Description, decimal BaseSalary);
