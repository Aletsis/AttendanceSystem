namespace AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;

using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

public sealed class ExternalEmployee : AggregateRoot<Guid>
{
    public BranchId BranchId { get; private set; } = null!;
    public string EmployeeNumber { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? Position { get; private set; }
    public string? Department { get; private set; }
    public EmployeeStatus Status { get; private set; }
    public string? CardNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ExternalEmployee() { } // Para EF Core

    public static ExternalEmployee Create(
        BranchId branchId,
        string employeeNumber,
        string firstName,
        string lastName,
        string? email = null,
        string? phoneNumber = null,
        string? position = null,
        string? department = null,
        EmployeeStatus status = EmployeeStatus.Alta,
        string? cardNumber = null)
    {
        if (branchId is null)
            throw new DomainException("La sucursal es requerida.");

        if (string.IsNullOrWhiteSpace(employeeNumber))
            throw new DomainException("El número de empleado es requerido.");

        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        return new ExternalEmployee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeNumber = employeeNumber.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim(),
            Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(),
            Status = status,
            CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        BranchId branchId,
        string employeeNumber,
        string firstName,
        string lastName,
        string? email,
        string? phoneNumber,
        string? position,
        string? department,
        EmployeeStatus status,
        string? cardNumber = null)
    {
        if (branchId is null)
            throw new DomainException("La sucursal es requerida.");

        if (string.IsNullOrWhiteSpace(employeeNumber))
            throw new DomainException("El número de empleado es requerido.");

        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        BranchId = branchId;
        EmployeeNumber = employeeNumber.Trim();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim();
        Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        Status = status;
        CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status = EmployeeStatus.Baja;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = EmployeeStatus.Alta;
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetFullName() => $"{FirstName} {LastName}".Trim();

    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException($"El campo {paramName} no puede estar vacío");

        if (name.Length > 100)
            throw new DomainException($"El campo {paramName} no puede exceder 100 caracteres");
    }

    private static void ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        if (email.Length > 255)
            throw new DomainException("El email no puede exceder 255 caracteres");

        if (!email.Contains('@') || !email.Contains('.'))
            throw new DomainException("El formato del email no es válido");
    }
}
