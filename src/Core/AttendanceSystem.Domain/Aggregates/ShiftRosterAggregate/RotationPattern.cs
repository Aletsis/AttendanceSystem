using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;

/// <summary>
/// Patrón rotativo vigente de un empleado. Se conserva para extender su roster: un patrón sin fecha fin (indefinido)
/// se proyecta continuamente y nunca deja días sin turno. La definición (esquema, turnos, descansos) la interpreta la
/// capa de aplicación; el dominio sólo guarda hasta dónde se generó.
/// </summary>
public sealed class RotationPattern : AggregateRoot<RotationPatternId>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime StartDate { get; private set; }
    /// <summary>Último día del patrón; null = indefinido.</summary>
    public DateTime? EndDate { get; private set; }
    public string Definition { get; private set; } = null!;
    public DateTime GeneratedThrough { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public bool IsIndefinite => EndDate == null;

    private RotationPattern() { }

    public static RotationPattern Create(EmployeeId employeeId, DateTime startDate, DateTime? endDate, string definition, DateTime generatedThrough)
    {
        if (employeeId == null) throw new ArgumentNullException(nameof(employeeId));
        if (string.IsNullOrWhiteSpace(definition)) throw new DomainException("El patrón rotativo debe tener una definición.");

        return new RotationPattern
        {
            Id = RotationPatternId.CreateNew(),
            EmployeeId = employeeId,
            StartDate = startDate.Date,
            EndDate = endDate?.Date,
            Definition = definition,
            GeneratedThrough = generatedThrough.Date,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Un esquema nuevo reemplaza al anterior desde su fecha de inicio.</summary>
    public void Replace(DateTime startDate, DateTime? endDate, string definition, DateTime generatedThrough)
    {
        if (string.IsNullOrWhiteSpace(definition)) throw new DomainException("El patrón rotativo debe tener una definición.");
        StartDate = startDate.Date;
        EndDate = endDate?.Date;
        Definition = definition;
        GeneratedThrough = generatedThrough.Date;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkGeneratedThrough(DateTime date)
    {
        if (date.Date <= GeneratedThrough) return;
        GeneratedThrough = date.Date;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>El empleado deja de ser rotativo: el patrón termina el día anterior.</summary>
    public void EndBefore(DateTime date)
    {
        var last = date.Date.AddDays(-1);
        if (EndDate.HasValue && EndDate.Value <= last) return;
        EndDate = last;
        if (GeneratedThrough > last) GeneratedThrough = last;
        UpdatedAt = DateTime.UtcNow;
    }
}
