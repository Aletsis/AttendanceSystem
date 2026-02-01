namespace AttendanceSystem.Application.DTOs;

public record WorkPeriodDto(string Name, DateTime Start, DateTime End)
{
    public override string ToString() => Name;
}
