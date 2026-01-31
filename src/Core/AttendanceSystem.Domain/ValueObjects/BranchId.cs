using AttendanceSystem.Domain.Primitives; // Assuming DomainException is here or GlobalUsings

namespace AttendanceSystem.Domain.ValueObjects;

public sealed record BranchId
{
    public Guid Value { get; }

    private BranchId(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("BranchId no puede ser vacío");
        
        Value = value;
    }

    public static BranchId CreateNew() => new(Guid.NewGuid());
    public static BranchId From(Guid value) => new(value);
    public static BranchId From(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString();
}
