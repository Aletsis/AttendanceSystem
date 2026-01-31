namespace AttendanceSystem.Domain.Enumerations;

public sealed class AttendanceStatus : Enumeration
{
    public static readonly AttendanceStatus Pending = new(1, "Pendiente");
    public static readonly AttendanceStatus Validated = new(2, "Validado");
    public static readonly AttendanceStatus Rejected = new(3, "Rechazado");
    public static readonly AttendanceStatus Duplicate = new(4, "Duplicado");
    public static readonly AttendanceStatus Processed = new(5, "Procesado");
    public static readonly AttendanceStatus Anomalous = new(6, "Anómalo");

    private AttendanceStatus(int id, string name) : base(id, name) { }

    public static AttendanceStatus FromValue(int value)
    {
        return value switch
        {
            1 => Pending,
            2 => Validated,
            3 => Rejected,
            4 => Duplicate,
            5 => Processed,
            6 => Anomalous,
            _ => throw new DomainException($"Estado de asistencia inválido: {value}")
        };
    }
}
