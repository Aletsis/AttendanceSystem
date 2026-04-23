namespace AttendanceSystem.Domain.Aggregates.BranchAggregate;

public sealed class Branch : AggregateRoot<BranchId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Address { get; private set; }
    public bool IsExternal { get; private set; }
    public string? ExternalHost { get; private set; }

    private Branch() { }

    public static Branch Create(string code, string name, string? address, bool isExternal = false, string? externalHost = null)
    {
        ValidateCode(code);
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        if (isExternal && string.IsNullOrWhiteSpace(externalHost))
            throw new DomainException("El host es requerido para sucursales externas.");

        return new Branch
        {
            Id = BranchId.CreateNew(),
            Code = code.ToUpper(),
            Name = name,
            Address = address,
            IsExternal = isExternal,
            ExternalHost = externalHost
        };
    }

    public void Update(string code, string name, string? address, bool isExternal = false, string? externalHost = null)
    {
        ValidateCode(code);
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sucursal es requerido.");

        if (isExternal && string.IsNullOrWhiteSpace(externalHost))
            throw new DomainException("El host es requerido para sucursales externas.");

        Code = code.ToUpper();
        Name = name;
        Address = address;
        IsExternal = isExternal;
        ExternalHost = externalHost;
    }

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
