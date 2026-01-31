namespace AttendanceSystem.Domain.ValueObjects;

public sealed record ShiftId
{
    public Guid Value { get; }

    private ShiftId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("ShiftId no puede ser vacío");
        
        Value = value;
    }

    public static ShiftId CreateNew() => new(Guid.NewGuid());
    public static ShiftId From(Guid value) => new(value);
    public static ShiftId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
