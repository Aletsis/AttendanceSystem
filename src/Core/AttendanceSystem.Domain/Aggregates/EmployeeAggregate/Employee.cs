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
            // Caso 12: sin extras autorizadas no hay redondeo, topes ni extras antes de la entrada; RH decide por día.
            OvertimeCalculationMethod = overtimeAuthorized ? overtimeCalculationMethod : OvertimeCalculationMethod.NoRounding,
            OvertimeCapType = overtimeAuthorized ? overtimeCapType : OvertimeCapType.None,
            OvertimeCapMinutes = overtimeAuthorized ? overtimeCapMinutes : null,
            CalculateOvertimeBeforeEntry = overtimeAuthorized && AllowsOvertimeBeforeEntry(shiftType) && calculateOvertimeBeforeEntry,
            CardNumber = cardNumber,
            DevicePassword = devicePassword,
            Photo = photo,
            DevicePrivilege = devicePrivilege,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = createdBy
        };

        employee._employmentPeriods.Add(new EmploymentPeriod(hireDate));
        employee._scheduleAssignments.Add(new ScheduleAssignment(hireDate, scheduleId, shiftType, branchId, employee.GetEffectiveRestDays()));
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

    /// <summary>
    /// Actualiza el empleado. Los cambios de horario, tipo de turno, sucursal y estado (baja / reingreso) valen
    /// desde <paramref name="effectiveFrom"/> (hoy si no se indica); en la baja es el último día laborado.
    /// Devuelve la primera fecha cuyo cálculo cambia, o null si ningún cambio afecta al cálculo.
    /// </summary>
    public DateTime? Update(
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
        IEnumerable<WeekDay>? restDays = null,
        DateTime? effectiveFrom = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        if (effectiveFrom.HasValue && effectiveFrom.Value.Date < hireDate.Date)
            throw new DomainException("La fecha de vigencia no puede ser anterior a la fecha de contratación");
        // Sin fecha explícita rige hoy, o el ingreso si aún no ocurre.
        var from = (effectiveFrom ?? (DateTime.Today < hireDate.Date ? hireDate : DateTime.Today)).Date;

        // F06-08: los cambios de horario, tipo, sucursal, días de descanso y de alta/baja quedan como vigencias desde
        // la fecha indicada.
        var newRestDays = ResolveRestDays(restDays, restDay);
        DateTime? recalculateFrom = null;
        if (RecordAssignment(from, scheduleId, shiftType, branchId, newRestDays)) recalculateFrom = from;
        if (HireDate.Date != hireDate.Date)
        {
            var earliest = hireDate.Date < HireDate.Date ? hireDate.Date : HireDate.Date;
            MoveHireDate(hireDate);
            recalculateFrom = recalculateFrom is { } r && r < earliest ? r : earliest;
        }
        if (Status != status)
        {
            if (from > DateTime.Today)
                throw new DomainException("La baja o el reingreso no pueden registrarse con fecha futura");
            if (status == EmployeeStatus.Baja) Terminate(from);
            else if (Status == EmployeeStatus.Baja)
            {
                var lastEnd = _employmentPeriods.Where(p => p.EndDate.HasValue).Select(p => p.EndDate!.Value).DefaultIfEmpty(TerminationDate ?? DateTime.MinValue).Max();
                if (from <= lastEnd)
                    throw new DomainException($"El reingreso debe ser posterior a la última baja ({lastEnd:dd/MM/yyyy})");
                Rehire(from);
            }
            recalculateFrom = recalculateFrom is { } r && r < from ? r : from;
        }

        RestDays = newRestDays;
        RestDay = newRestDays.Count > 0 ? newRestDays[0] : null;

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
        // Caso 12: sin extras autorizadas no hay redondeo, topes ni extras antes de la entrada; RH decide por día.
        OvertimeCalculationMethod = overtimeAuthorized ? overtimeCalculationMethod : OvertimeCalculationMethod.NoRounding;
        OvertimeCapType = overtimeAuthorized ? overtimeCapType : OvertimeCapType.None;
        OvertimeCapMinutes = overtimeAuthorized ? overtimeCapMinutes : null;
        CalculateOvertimeBeforeEntry = overtimeAuthorized && AllowsOvertimeBeforeEntry(shiftType) && calculateOvertimeBeforeEntry;
        CardNumber = cardNumber;
        DevicePassword = devicePassword;
        Photo = photo ?? Photo;
        DevicePrivilege = devicePrivilege;
        return recalculateFrom;
    }

    /// <summary>Días de descanso efectivos de una actualización: la lista, o el día principal heredado si viene vacía.</summary>
    private static List<WeekDay> ResolveRestDays(IEnumerable<WeekDay>? restDays, WeekDay? restDay)
    {
        var configured = restDays?.Distinct().ToList();
        if (configured is { Count: > 0 }) return configured;
        return restDay.HasValue ? new List<WeekDay> { restDay.Value } : new List<WeekDay>();
    }

    /// <summary>Descanso según los días vigentes en <paramref name="date"/> (no los actuales).</summary>
    public bool IsRestDayOn(DateTime date) => RestDaysOn(date).Contains((WeekDay)(int)date.DayOfWeek);

    /// <summary>Días de descanso vigentes en una fecha; los actuales si la vigencia es anterior a que se fecharan.</summary>
    public IReadOnlyList<WeekDay> RestDaysOn(DateTime date) => EffectiveAssignment(date)?.RestDays ?? GetEffectiveRestDays();

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

    /// <summary>F07-03: perteneció a la sucursal algún día de [inicio, fin] según sus vigencias (o su sucursal actual sin historia).</summary>
    public bool WasInBranchDuring(BranchId branchId, DateTime start, DateTime end)
    {
        if (_scheduleAssignments.Count == 0) return BranchId == branchId;
        var ordered = _scheduleAssignments.OrderBy(a => a.EffectiveFrom).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var from = i == 0 ? DateTime.MinValue : ordered[i].EffectiveFrom;
            var until = i + 1 < ordered.Count ? ordered[i + 1].EffectiveFrom.AddDays(-1) : DateTime.MaxValue.Date;
            if (ordered[i].BranchId == branchId && from <= end.Date && until >= start.Date) return true;
        }
        return false;
    }

    /// <summary>F06-08: horario, tipo y sucursal vigentes en una fecha (la asignación actual si no hay historia).</summary>
    public (ShiftId? ScheduleId, ShiftType? ShiftType, BranchId BranchId) AssignmentOn(DateTime date)
    {
        var effective = EffectiveAssignment(date);
        return effective == null
            ? (ScheduleId, ShiftType, BranchId)
            : (effective.ScheduleId, effective.ShiftType, effective.BranchId);
    }

    private ScheduleAssignment? EffectiveAssignment(DateTime date) =>
        _scheduleAssignments
            .Where(a => a.EffectiveFrom <= date.Date)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefault()
        ?? _scheduleAssignments.OrderBy(a => a.EffectiveFrom).FirstOrDefault();

    /// <summary>
    /// Registra una asignación vigente desde <paramref name="effectiveFrom"/>; devuelve si cambió algo. Las
    /// vigencias posteriores se conservan: el cambio rige hasta la siguiente. Sin <paramref name="restDays"/> se
    /// conservan los días de descanso vigentes en esa fecha.
    /// </summary>
    public bool RecordAssignment(DateTime effectiveFrom, ShiftId? scheduleId, ShiftType? shiftType, BranchId branchId, IEnumerable<WeekDay>? restDays = null)
    {
        if (_scheduleAssignments.Count == 0)
        {
            // Sin historia previa: la configuración actual rige desde el ingreso (límite documentado).
            _scheduleAssignments.Add(new ScheduleAssignment(HireDate, ScheduleId, ShiftType, BranchId, GetEffectiveRestDays()));
        }

        var current = EffectiveAssignment(effectiveFrom)!;
        restDays ??= current.RestDays ?? GetEffectiveRestDays();
        if (current.SameAs(scheduleId, shiftType, branchId, restDays)) return false;

        var sameDay = _scheduleAssignments.FirstOrDefault(a => a.EffectiveFrom == effectiveFrom.Date);
        if (sameDay != null) sameDay.Replace(scheduleId, shiftType, branchId, restDays);
        else _scheduleAssignments.Add(new ScheduleAssignment(effectiveFrom, scheduleId, shiftType, branchId, restDays));
        return true;
    }

    /// <summary>
    /// El empleado pasa a turno rotativo desde <paramref name="effectiveFrom"/> (al generarle un roster); conserva
    /// horario, sucursal y descansos. Devuelve si cambió algo.
    /// </summary>
    public bool BecomeRotating(DateTime effectiveFrom)
    {
        var (scheduleId, _, branchId) = AssignmentOn(effectiveFrom);
        var changed = RecordAssignment(effectiveFrom, scheduleId, Enumerations.ShiftType.Rotativo, branchId);
        if (effectiveFrom.Date <= DateTime.Today) ShiftType = Enumerations.ShiftType.Rotativo;
        return changed;
    }

    private void MoveHireDate(DateTime hireDate)
    {
        var first = _employmentPeriods.OrderBy(p => p.StartDate).FirstOrDefault();
        if (first != null && _employmentPeriods.Count == 1) first.MoveStart(hireDate);
        var firstAssignment = _scheduleAssignments.OrderBy(a => a.EffectiveFrom).FirstOrDefault();
        if (firstAssignment != null && _scheduleAssignments.Count == 1)
        {
            _scheduleAssignments.Clear();
            _scheduleAssignments.Add(new ScheduleAssignment(hireDate, firstAssignment.ScheduleId, firstAssignment.ShiftType, firstAssignment.BranchId,
                firstAssignment.RestDays ?? GetEffectiveRestDays().ToList()));
        }
    }

    public void SetStatus(EmployeeStatus status)
    {
        Status = status;
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

    /// <summary>
    /// Caso 12: el horario flexible ya define desde cuándo cuenta el tiempo (ventana de entrada), así que no admite
    /// extras antes de la entrada.
    /// </summary>
    public static bool AllowsOvertimeBeforeEntry(ShiftType? shiftType) => shiftType != Enumerations.ShiftType.Flexible;

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
