namespace AttendanceSystem.Domain.ValueObjects;

public sealed record RotationPatternId
{
    public Guid Value { get; }

    private RotationPatternId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("RotationPatternId no puede ser vacío");

        Value = value;
    }

    public static RotationPatternId CreateNew() => new(Guid.NewGuid());
    public static RotationPatternId From(Guid value) => new(value);
    public static RotationPatternId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
