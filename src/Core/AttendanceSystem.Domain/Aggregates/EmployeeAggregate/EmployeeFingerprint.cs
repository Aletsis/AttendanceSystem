namespace AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Primitives;

public class EmployeeFingerprint : Entity<int>
{
    public int FingerIndex { get; private set; } // 0-9
    public string Template { get; private set; } = string.Empty;
    public EmployeeId EmployeeId { get; private set; } = null!;

    private EmployeeFingerprint() { }

    public EmployeeFingerprint(int fingerIndex, string template)
    {
       FingerIndex = fingerIndex;
       Template = template;
    }
}
