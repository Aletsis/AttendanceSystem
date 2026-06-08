namespace AttendanceSystem.Domain.ValueObjects;

public sealed record SystemAlertId
{
    public Guid Value { get; }

    private SystemAlertId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("SystemAlertId no puede ser vacío");
        
        Value = value;
    }

    public static SystemAlertId CreateNew() => new(Guid.NewGuid());
    public static SystemAlertId From(Guid value) => new(value);
    public static SystemAlertId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
