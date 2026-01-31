namespace AttendanceSystem.Domain.Enumerations;

public sealed class CheckType : Enumeration
{
    public static readonly CheckType CheckIn = new(0, "Entrada");
    public static readonly CheckType CheckOut = new(1, "Salida");
    public static readonly CheckType BreakStart = new(2, "Inicio de Descanso");
    public static readonly CheckType BreakEnd = new(3, "Fin de Descanso");
    public static readonly CheckType OvertimeIn = new(4, "Inicio Tiempo Extra");
    public static readonly CheckType OvertimeOut = new(5, "Fin Tiempo Extra");

    private CheckType(int id, string name) : base(id, name) { }

    public static CheckType FromValue(int value)
    {
        return value switch
        {
            0 => CheckIn,
            1 => CheckOut,
            2 => BreakStart,
            3 => BreakEnd,
            4 => OvertimeIn,
            5 => OvertimeOut,
            _ => throw new DomainException($"Tipo de checada inválido: {value}")
        };
    }
}