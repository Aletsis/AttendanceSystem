public sealed class VerifyMethod : Enumeration
{
    public static readonly VerifyMethod Fingerprint = new(1, "Huella Dactilar");
    public static readonly VerifyMethod RFIDCard = new(2, "Tarjeta RFID");
    public static readonly VerifyMethod Password = new(3, "Contraseña");
    public static readonly VerifyMethod FaceRecognition = new(15, "Reconocimiento Facial");
    public static readonly VerifyMethod Manual = new(99, "Manual");
    public static readonly VerifyMethod Other = new(0, "Otro");

    private VerifyMethod(int id, string name) : base(id, name) { }

    public static VerifyMethod FromValue(int value)
    {
        return value switch
        {
            1 => Fingerprint,
            2 => RFIDCard,
            3 => Password,
            15 => FaceRecognition,
            99 => Manual,
            0 => Other,
            _ => throw new DomainException($"Método de verificación inválido: {value}")
        };
    }
}
// Base class para enumeraciones
public abstract class Enumeration : IComparable
{
    public int Id { get; }
    public string Name { get; }

    protected Enumeration(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
    
    public int CompareTo(object? other)
    {
        return other is Enumeration enumeration 
            ? Id.CompareTo(enumeration.Id) 
            : throw new ArgumentException("Object is not an Enumeration");
    }
}