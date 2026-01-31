namespace AttendanceSystem.Domain.ValueObjects;

public sealed record DepartmentId
{
    public Guid Value { get; }

    private DepartmentId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("DepartmentId no puede ser vacío");
        
        Value = value;
    }

    public static DepartmentId CreateNew() => new(Guid.NewGuid());
    public static DepartmentId From(Guid value) => new(value);
    public static DepartmentId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
