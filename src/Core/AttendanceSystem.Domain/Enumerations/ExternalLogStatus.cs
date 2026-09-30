using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Enumerations;

public sealed class ExternalLogStatus : Enumeration
{
    public static readonly ExternalLogStatus Pending = new(1, "Pendiente");
    public static readonly ExternalLogStatus Transferred = new(2, "Transferido");
    public static readonly ExternalLogStatus Failed = new(3, "Fallido");

    private ExternalLogStatus(int id, string name) : base(id, name) { }

    public static ExternalLogStatus FromValue(int value)
    {
        return value switch
        {
            1 => Pending,
            2 => Transferred,
            3 => Failed,
            _ => throw new DomainException($"Estado de log externo inválido: {value}")
        };
    }
}
