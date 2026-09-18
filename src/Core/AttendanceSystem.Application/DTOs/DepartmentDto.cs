namespace AttendanceSystem.Application.DTOs;

public record DepartmentDto(Guid Id, string Name, string? Description, List<Guid>? PositionIds = null)
{
    public DepartmentDto() : this(Guid.Empty, string.Empty, null, null) { }
}
