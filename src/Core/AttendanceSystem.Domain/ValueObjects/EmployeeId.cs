namespace AttendanceSystem.Domain.ValueObjects;

public sealed record EmployeeId
{
    public string Value { get; }

    private EmployeeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("EmployeeId no puede estar vacío");
        
        if (value.Length > 20)
            throw new DomainException("EmployeeId no puede exceder 20 caracteres");
        
        Value = value;
    }

    public static EmployeeId From(string value) => new(value);
    
    public override string ToString() => Value;
}