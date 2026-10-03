namespace AttendanceSystem.Domain.ValueObjects;

public sealed record ShiftRosterId
{
    public Guid Value { get; }

    private ShiftRosterId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("ShiftRosterId no puede ser vacío");

        Value = value;
    }

    public static ShiftRosterId CreateNew() => new(Guid.NewGuid());
    public static ShiftRosterId From(Guid value) => new(value);
    public static ShiftRosterId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
