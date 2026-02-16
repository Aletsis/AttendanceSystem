namespace AttendanceSystem.Domain.Aggregates.PositionAggregate;

public sealed class Position : AggregateRoot<PositionId>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    
    // Often Puestos have a default salary range or level, but we stick to basic info.
    public decimal BaseSalary { get; private set; } 

    private Position() { }

    public static Position Create(string name, string? description, decimal baseSalary)
    {
         if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del puesto es requerido.");

        return new Position
        {
            Id = PositionId.CreateNew(),
            Name = name,
            Description = description,
            BaseSalary = baseSalary
        };
    }

    public void Update(string name, string? description, decimal baseSalary)
    {
         if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del puesto es requerido.");

        Name = name;
        Description = description;
        BaseSalary = baseSalary;
    }
}
