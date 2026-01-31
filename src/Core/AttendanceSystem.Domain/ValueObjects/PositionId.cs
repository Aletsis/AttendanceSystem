namespace AttendanceSystem.Domain.ValueObjects;

public sealed record PositionId
{
    public Guid Value { get; }

    private PositionId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("PositionId no puede ser vacío");
        
        Value = value;
    }

    public static PositionId CreateNew() => new(Guid.NewGuid());
    public static PositionId From(Guid value) => new(value);
    public static PositionId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
