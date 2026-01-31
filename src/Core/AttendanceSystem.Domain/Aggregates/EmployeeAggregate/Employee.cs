namespace AttendanceSystem.Domain.Aggregates.EmployeeAggregate;

using AttendanceSystem.Domain.Enumerations;

public sealed class Employee : AggregateRoot<EmployeeId>
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime HireDate { get; private set; }
    public EmployeeStatus Status { get; private set; }
    public Gender Gender { get; private set; } // Nuevo campo
    
    // Relaciones con otros agregados
    public BranchId BranchId { get; private set; } = null!;
    public DepartmentId DepartmentId { get; private set; } = null!;
    public PositionId PositionId { get; private set; } = null!;
    
    // Horario y configuración laboral
    public ShiftType? ShiftType { get; private set; }
    public ShiftId? ScheduleId { get; private set; } // Horario específico (puede ser diferente al turno)
    public WeekDay? RestDay { get; private set; } // Día de descanso
    public bool OvertimeAuthorized { get; private set; } // Horas extras autorizadas
    public OvertimeCalculationMethod OvertimeCalculationMethod { get; private set; } // Nuevo campo
    public OvertimeCapType OvertimeCapType { get; private set; }
    public double? OvertimeCapMinutes { get; private set; }

    // Datos biométricos y de acceso
    public string? CardNumber { get; private set; }
    public string? DevicePassword { get; private set; }
    private readonly List<EmployeeFingerprint> _fingerprints = new();
    public IReadOnlyCollection<EmployeeFingerprint> Fingerprints => _fingerprints.AsReadOnly();
    public string? FaceTemplate { get; private set; }

    private Employee() { } // Para EF Core

    public static Employee Create(
        EmployeeId id,
        string firstName,
        string lastName,
        string? email,
        string? phoneNumber,
        DateTime hireDate,
        Gender gender,
        BranchId branchId,
        DepartmentId departmentId,
        PositionId positionId,
        ShiftType? shiftType,
        ShiftId? scheduleId = null,
        WeekDay? restDay = null,
        bool overtimeAuthorized = false,
        OvertimeCalculationMethod overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding,
        OvertimeCapType overtimeCapType = OvertimeCapType.None,
        double? overtimeCapMinutes = null,
        string? cardNumber = null,
        string? devicePassword = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        return new Employee
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            PhoneNumber = phoneNumber,
            HireDate = hireDate,
            Gender = gender,
            Status = EmployeeStatus.Alta,
            BranchId = branchId,
            DepartmentId = departmentId,
            PositionId = positionId,
            ShiftType = shiftType,
            ScheduleId = scheduleId,
            RestDay = restDay,
            OvertimeAuthorized = overtimeAuthorized,
            OvertimeCalculationMethod = overtimeCalculationMethod,
            OvertimeCapType = overtimeCapType,
            OvertimeCapMinutes = overtimeCapMinutes,
            CardNumber = cardNumber,
            DevicePassword = devicePassword
        };
    }

    public void UpdateBiometrics(string? cardNumber, string? devicePassword, string? faceTemplate, List<EmployeeFingerprint> fingerprints)
    {
        CardNumber = cardNumber;
        DevicePassword = devicePassword;
        FaceTemplate = faceTemplate;
        
        _fingerprints.Clear();
        _fingerprints.AddRange(fingerprints);
    }

    public void Update(
        string firstName,
        string lastName,
        string? email,
        string? phoneNumber,
        DateTime hireDate,
        Gender gender,
        EmployeeStatus status,
        BranchId branchId,
        DepartmentId departmentId,
        PositionId positionId,
        ShiftType? shiftType,
        ShiftId? scheduleId = null,
        WeekDay? restDay = null,
        bool overtimeAuthorized = false,
        OvertimeCalculationMethod overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding,
        OvertimeCapType overtimeCapType = OvertimeCapType.None,
        double? overtimeCapMinutes = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        FirstName = firstName;
        LastName = lastName;
        Email = string.IsNullOrWhiteSpace(email) ? null : email;
        PhoneNumber = phoneNumber;
        HireDate = hireDate;
        Gender = gender;
        Status = status;
        BranchId = branchId;
        DepartmentId = departmentId;
        PositionId = positionId;
        ShiftType = shiftType;
        ScheduleId = scheduleId;
        RestDay = restDay;
        OvertimeAuthorized = overtimeAuthorized;
        OvertimeCalculationMethod = overtimeCalculationMethod;
        OvertimeCapType = overtimeCapType;
        OvertimeCapMinutes = overtimeCapMinutes;
    }

    public void Deactivate()
    {
        Status = EmployeeStatus.Baja;
    }

    public void Activate()
    {
        Status = EmployeeStatus.Alta;
    }

    public void SetStatus(EmployeeStatus status)
    {
        Status = status;
    }

    public string GetFullName() => $"{FirstName} {LastName}";

    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException($"El {paramName} no puede estar vacío");

        if (name.Length > 100)
            throw new DomainException($"El {paramName} no puede exceder 100 caracteres");
    }

    private static void ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return; // Email es opcional

        if (email.Length > 255)
            throw new DomainException("El email no puede exceder 255 caracteres");

        // Validación básica de formato de email
        if (!email.Contains('@') || !email.Contains('.'))
            throw new DomainException("El formato del email no es válido");
    }
}
