namespace AttendanceSystem.Domain.ValueObjects;

public sealed record DeviceId
{
    public string Value { get; }

    private DeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("DeviceId no puede estar vacío");
        
        Value = value;
    }

    public static DeviceId From(string value) => new(value);
    
    public override string ToString() => Value;
}