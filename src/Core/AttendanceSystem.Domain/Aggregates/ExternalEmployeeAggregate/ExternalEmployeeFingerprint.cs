using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;

/// <summary>Plantilla de una huella copiada del reloj local, para volver a enviarla cuando se reenvía al empleado.</summary>
public class ExternalEmployeeFingerprint : Entity<int>
{
    public int FingerIndex { get; private set; } // 0-9
    public string Template { get; private set; } = string.Empty;

    private ExternalEmployeeFingerprint() { }

    public ExternalEmployeeFingerprint(int fingerIndex, string template)
    {
        FingerIndex = fingerIndex;
        Template = template;
    }
}
