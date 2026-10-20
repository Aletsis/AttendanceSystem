namespace AttendanceSystem.Domain.Aggregates.EmployeeAggregate;

using AttendanceSystem.Domain.Enumerations;

public sealed class Employee : AggregateRoot<EmployeeId>
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime HireDate { get; private set; }
    public DateTime? TerminationDate { get; private set; }
    public EmployeeStatus Status { get; private set; }
    public Gender Gender { get; private set; } // Nuevo campo

    // Auditoría de registro y modificaciones
    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public DevicePrivilege DevicePrivilege { get; private set; }

    // Relaciones con otros agregados
    public BranchId BranchId { get; private set; } = null!;
    public DepartmentId DepartmentId { get; private set; } = null!;
    public PositionId PositionId { get; private set; } = null!;

    // Horario y configuración laboral
    public ShiftType? ShiftType { get; private set; }
    public ShiftId? ScheduleId { get; private set; } // Horario específico (puede ser diferente al turno)
    public WeekDay? RestDay { get; private set; } // Día de descanso principal (mantenido por compatibilidad)
    public List<WeekDay> RestDays { get; private set; } = new(); // Días de descanso
    public bool OvertimeAuthorized { get; private set; } // Horas extras autorizadas
    public OvertimeCalculationMethod OvertimeCalculationMethod { get; private set; } // Nuevo campo
    public OvertimeCapType OvertimeCapType { get; private set; }
    public double? OvertimeCapMinutes { get; private set; }
    public bool CalculateOvertimeBeforeEntry { get; private set; } // Nuevo campo

    // Datos biométricos y de acceso
    public string? CardNumber { get; private set; }
    public string? DevicePassword { get; private set; }
    private readonly List<EmployeeFingerprint> _fingerprints = new();
    public IReadOnlyCollection<EmployeeFingerprint> Fingerprints => _fingerprints.AsReadOnly();
    public string? FaceTemplate { get; private set; }
    public string? Photo { get; private set; }

    // F06-08: vigencias. Sin historia registrada (datos anteriores) se usan HireDate/TerminationDate y la
    // asignación actual; no se inventan períodos.
    private readonly List<EmploymentPeriod> _employmentPeriods = new();
    public IReadOnlyCollection<EmploymentPeriod> EmploymentPeriods => _employmentPeriods.AsReadOnly();

    private readonly List<ScheduleAssignment> _scheduleAssignments = new();
    public IReadOnlyCollection<ScheduleAssignment> ScheduleAssignments => _scheduleAssignments.AsReadOnly();

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
        bool calculateOvertimeBeforeEntry = false,
        string? cardNumber = null,
        string? devicePassword = null,
        string? photo = null,
        DevicePrivilege devicePrivilege = DevicePrivilege.User,
        IEnumerable<WeekDay>? restDays = null,
        string? createdBy = null,
        DateTime? createdAt = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        var configuredRestDays = restDays?.Distinct().ToList() ?? new List<WeekDay>();
        if (!configuredRestDays.Any() && restDay.HasValue)
        {
            configuredRestDays.Add(restDay.Value);
        }
        var primaryRestDay = configuredRestDays.Count > 0 ? (WeekDay?)configuredRestDays[0] : restDay;

        var employee = new Employee
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
            RestDay = primaryRestDay,
            RestDays = configuredRestDays,
            OvertimeAuthorized = overtimeAuthorized,
            OvertimeCalculationMethod = overtimeCalculationMethod,
            OvertimeCapType = overtimeCapType,
            OvertimeCapMinutes = overtimeCapMinutes,
            CalculateOvertimeBeforeEntry = calculateOvertimeBeforeEntry,
            CardNumber = cardNumber,
            DevicePassword = devicePassword,
            Photo = photo,
            DevicePrivilege = devicePrivilege,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = createdBy
        };

        employee._employmentPeriods.Add(new EmploymentPeriod(hireDate));
        employee._scheduleAssignments.Add(new ScheduleAssignment(hireDate, scheduleId, shiftType, branchId));
        return employee;
    }

    public void UpdateBiometrics(
        string? cardNumber = null,
        string? devicePassword = null,
        string? faceTemplate = null,
        List<EmployeeFingerprint>? fingerprints = null,
        string? photo = null)
    {
        if (cardNumber != null) CardNumber = cardNumber;
        if (devicePassword != null) DevicePassword = devicePassword;
        if (faceTemplate != null) FaceTemplate = faceTemplate;
        if (photo != null) Photo = photo;

        if (fingerprints != null)
        {
            foreach (var fp in fingerprints)
            {
                var existing = _fingerprints.FirstOrDefault(f => f.FingerIndex == fp.FingerIndex);
                if (existing != null)
                {
                    _fingerprints.Remove(existing);
                }
                _fingerprints.Add(fp);
            }
        }
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
        double? overtimeCapMinutes = null,
        bool calculateOvertimeBeforeEntry = false,
        string? cardNumber = null,
        string? devicePassword = null,
        string? photo = null,
        DevicePrivilege devicePrivilege = DevicePrivilege.User,
        IEnumerable<WeekDay>? restDays = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        var configuredRestDays = restDays?.Distinct().ToList();
        if (configuredRestDays != null && configuredRestDays.Any())
        {
            RestDays = configuredRestDays;
            RestDay = configuredRestDays.FirstOrDefault();
        }
        else if (restDays != null)
        {
            RestDays = new List<WeekDay>();
            RestDay = restDay;
        }
        else if (restDay.HasValue)
        {
            RestDays = new List<WeekDay> { restDay.Value };
            RestDay = restDay;
        }
        else
        {
            RestDays = new List<WeekDay>();
            RestDay = null;
        }

        // F06-08: los cambios de horario, tipo o sucursal y de alta/baja quedan como vigencias desde hoy.
        RecordAssignment(DateTime.Today, scheduleId, shiftType, branchId);
        if (HireDate.Date != hireDate.Date) MoveHireDate(hireDate);
        if (Status != status)
        {
            if (status == EmployeeStatus.Baja) Terminate(DateTime.Today);
            else if (Status == EmployeeStatus.Baja) Rehire(DateTime.Today);
        }

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
        OvertimeAuthorized = overtimeAuthorized;
        OvertimeCalculationMethod = overtimeCalculationMethod;
        OvertimeCapType = overtimeCapType;
        OvertimeCapMinutes = overtimeCapMinutes;
        CalculateOvertimeBeforeEntry = calculateOvertimeBeforeEntry;
        CardNumber = cardNumber;
        DevicePassword = devicePassword;
        Photo = photo ?? Photo;
        DevicePrivilege = devicePrivilege;
    }

    public bool IsRestDay(DayOfWeek dayOfWeek)
    {
        var wd = (WeekDay)(int)dayOfWeek;
        return IsRestDay(wd);
    }

    public bool IsRestDay(WeekDay dayOfWeek)
    {
        if (RestDays != null && RestDays.Count > 0)
        {
            return RestDays.Contains(dayOfWeek);
        }
        return RestDay.HasValue && RestDay.Value == dayOfWeek;
    }

    public IReadOnlyList<WeekDay> GetEffectiveRestDays()
    {
        if (RestDays != null && RestDays.Count > 0)
        {
            return RestDays.Distinct().OrderBy(d => (int)d).ToList();
        }
        if (RestDay.HasValue)
        {
            return new List<WeekDay> { RestDay.Value };
        }
        return Array.Empty<WeekDay>();
    }

    public void UpdateDevicePrivilege(DevicePrivilege devicePrivilege)
    {
        DevicePrivilege = devicePrivilege;
    }

    public void Deactivate(DateTime? terminationDate = null)
    {
        Terminate(terminationDate?.Date ?? (TerminationDate ?? DateTime.UtcNow.Date));
    }

    /// <summary>Reactiva al empleado; si estaba de baja es un reingreso desde hoy.</summary>
    public void Activate() => Rehire(DateTime.Today);

    public void Terminate(DateTime terminationDate)
    {
        var open = _employmentPeriods.LastOrDefault(p => !p.EndDate.HasValue);
        open?.Close(terminationDate);
        TerminationDate = terminationDate.Date;
        Status = EmployeeStatus.Baja;
    }

    /// <summary>F06-08: reingreso. Abre un período nuevo; la interrupción entre períodos no es laborable.</summary>
    public void Rehire(DateTime rehireDate)
    {
        if (_employmentPeriods.Count == 0)
        {
            // Datos anteriores sin períodos: se conserva el primero conocido antes de abrir el nuevo.
            _employmentPeriods.Add(new EmploymentPeriod(HireDate, TerminationDate ?? rehireDate.AddDays(-1)));
        }

        if (!_employmentPeriods.Any(p => !p.EndDate.HasValue))
        {
            _employmentPeriods.Add(new EmploymentPeriod(rehireDate));
        }

        TerminationDate = null;
        Status = EmployeeStatus.Alta;
    }

    public bool IsActiveOn(DateTime date)
    {
        if (_employmentPeriods.Count > 0)
        {
            return _employmentPeriods.Any(p => p.Contains(date));
        }

        var targetDate = date.Date;
        if (targetDate < HireDate.Date)
            return false;

        if (TerminationDate.HasValue && targetDate > TerminationDate.Value.Date)
            return false;

        return true;
    }

    /// <summary>Hubo relación laboral en algún día de [inicio, fin].</summary>
    public bool IsActiveDuring(DateTime start, DateTime end)
    {
        if (_employmentPeriods.Count > 0)
        {
            return _employmentPeriods.Any(p => p.Overlaps(start, end));
        }
        return HireDate.Date <= end.Date && (!TerminationDate.HasValue || TerminationDate.Value.Date >= start.Date);
    }

    /// <summary>F06-08: horario, tipo y sucursal vigentes en una fecha (la asignación actual si no hay historia).</summary>
    public (ShiftId? ScheduleId, ShiftType? ShiftType, BranchId BranchId) AssignmentOn(DateTime date)
    {
        var effective = _scheduleAssignments
            .Where(a => a.EffectiveFrom <= date.Date)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefault()
            ?? _scheduleAssignments.OrderBy(a => a.EffectiveFrom).FirstOrDefault();

        return effective == null
            ? (ScheduleId, ShiftType, BranchId)
            : (effective.ScheduleId, effective.ShiftType, effective.BranchId);
    }

    /// <summary>Registra una asignación vigente desde <paramref name="effectiveFrom"/> si cambia algo.</summary>
    public void RecordAssignment(DateTime effectiveFrom, ShiftId? scheduleId, ShiftType? shiftType, BranchId branchId)
    {
        if (_scheduleAssignments.Count == 0)
        {
            // Sin historia previa: la configuración actual rige desde el ingreso (límite documentado).
            _scheduleAssignments.Add(new ScheduleAssignment(HireDate, ScheduleId, ShiftType, BranchId));
        }

        var current = AssignmentOn(effectiveFrom);
        if (current.ScheduleId == scheduleId && current.ShiftType == shiftType && current.BranchId == branchId) return;

        var sameDay = _scheduleAssignments.FirstOrDefault(a => a.EffectiveFrom == effectiveFrom.Date);
        if (sameDay != null) sameDay.Replace(scheduleId, shiftType, branchId);
        else _scheduleAssignments.Add(new ScheduleAssignment(effectiveFrom, scheduleId, shiftType, branchId));
    }

    private void MoveHireDate(DateTime hireDate)
    {
        var first = _employmentPeriods.OrderBy(p => p.StartDate).FirstOrDefault();
        if (first != null && _employmentPeriods.Count == 1) first.MoveStart(hireDate);
        var firstAssignment = _scheduleAssignments.OrderBy(a => a.EffectiveFrom).FirstOrDefault();
        if (firstAssignment != null && _scheduleAssignments.Count == 1)
        {
            _scheduleAssignments.Clear();
            _scheduleAssignments.Add(new ScheduleAssignment(hireDate, firstAssignment.ScheduleId, firstAssignment.ShiftType, firstAssignment.BranchId));
        }
    }

    public void SetStatus(EmployeeStatus status)
    {
        Status = status;
    }

    public void SetShiftType(ShiftType? shiftType)
    {
        ShiftType = shiftType;
    }

    public void SetCreatedAudit(DateTime createdAt, string? createdBy)
    {
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public void SetUpdatedAudit(DateTime updatedAt, string? updatedBy)
    {
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
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
