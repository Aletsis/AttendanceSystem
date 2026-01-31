namespace AttendanceSystem.Domain.ValueObjects;

public sealed record AttendanceRecordId
{
    public Guid Value { get; }

    private AttendanceRecordId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("AttendanceRecordId no puede ser vacío");
        
        Value = value;
    }

    public static AttendanceRecordId CreateNew() => new(Guid.NewGuid());
    public static AttendanceRecordId From(Guid value) => new(value);
    public static AttendanceRecordId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}