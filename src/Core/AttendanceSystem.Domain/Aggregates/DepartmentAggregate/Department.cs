using AttendanceSystem.Domain.Aggregates.PositionAggregate;

namespace AttendanceSystem.Domain.Aggregates.DepartmentAggregate;

public sealed class Department : AggregateRoot<DepartmentId>
{
    public string Name { get; private set; }
    public string? Description { get; private set; }

    public ICollection<Position> Positions { get; private set; } = new List<Position>();

    private Department() { }

    public static Department Create(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del departamento es requerido.");

        return new Department
        {
            Id = DepartmentId.CreateNew(),
            Name = name,
            Description = description
        };
    }

    public void Update(string name, string? description)
    {
         if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del departamento es requerido.");

        Name = name;
        Description = description;
    }

    public void AddPosition(Position position)
    {
        if (!Positions.Contains(position))
        {
            Positions.Add(position);
        }
    }

    public void RemovePosition(Position position)
    {
        if (Positions.Contains(position))
        {
            Positions.Remove(position);
        }
    }

    public void SetPositions(IEnumerable<Position> positions)
    {
        Positions.Clear();
        foreach (var position in positions)
        {
            Positions.Add(position);
        }
    }
}
