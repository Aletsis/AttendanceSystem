namespace AttendanceSystem.Domain.Aggregates.BranchAggregate;

public sealed class Branch : AggregateRoot<BranchId>
{
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? Address { get; private set; }

    private Branch() { }

    public static Branch Create(string name, string? description, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        return new Branch
        {
            Id = BranchId.CreateNew(),
            Name = name,
            Description = description,
            Address = address
        };
    }

    public void Update(string name, string? description, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        Name = name;
        Description = description;
        Address = address;
    }
}
