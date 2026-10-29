namespace AttendanceSystem.Domain.Aggregates.BranchAggregate;

public sealed class Branch : AggregateRoot<BranchId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Address { get; private set; }
    public bool IsExternal { get; private set; }
    public string? ExternalHost { get; private set; }
    public string? TimeZoneId { get; private set; }

    public void SetTimeZone(string? timeZoneId)
    {
        TimeZoneId = timeZoneId;
    }

    private Branch() { }

    public static Branch Create(string code, string name, string? address, bool isExternal = false, string? externalHost = null, string? timeZoneId = null)
    {
        ValidateCode(code);
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        return new Branch
        {
            Id = BranchId.CreateNew(),
            Code = code.ToUpper(),
            Name = name,
            Address = address,
            IsExternal = isExternal,
            ExternalHost = NormalizeExternalHost(isExternal, externalHost),
            TimeZoneId = timeZoneId
        };
    }

    public void Update(string code, string name, string? address, bool isExternal = false, string? externalHost = null, string? timeZoneId = null)
    {
        // El código no cambia: es el prefijo de los IDs en los relojes y el que identifica las checadas en la nube.
        if (!string.Equals(code?.Trim(), Code, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("El código de la sucursal no se puede cambiar una vez creada.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        Name = name;
        Address = address;
        IsExternal = isExternal;
        ExternalHost = NormalizeExternalHost(isExternal, externalHost);
        if (timeZoneId != null)
        {
            TimeZoneId = timeZoneId;
        }
    }

    // El host de la API es opcional: una sucursal externa puede sincronizar solo por la BD en la nube.
    private static string? NormalizeExternalHost(bool isExternal, string? externalHost) =>
        isExternal && !string.IsNullOrWhiteSpace(externalHost) ? externalHost.Trim() : null;

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("El código de la sucursal es requerido.");

        if (code.Length != 3)
            throw new DomainException("El código de la sucursal debe tener exactamente 3 caracteres (Ej: A01).");

        if (!char.IsLetter(code[0]) || !char.IsDigit(code[1]) || !char.IsDigit(code[2]))
            throw new DomainException("El código de la sucursal debe tener el formato: 1 letra y 2 números (Ej: A01).");
    }
}
