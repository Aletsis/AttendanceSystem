using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using System.Text.Json;
using AttendanceSystem.Infrastructure.Persistence.Outbox;

namespace AttendanceSystem.Infrastructure.Persistence;

public class AttendanceDbContext : IdentityDbContext<ApplicationUser>, IUnitOfWork
{
    private readonly IPublisher _publisher;

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch> Branches => Set<AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch>();
    public DbSet<AttendanceSystem.Domain.Aggregates.DepartmentAggregate.Department> Departments => Set<AttendanceSystem.Domain.Aggregates.DepartmentAggregate.Department>();
    public DbSet<AttendanceSystem.Domain.Aggregates.PositionAggregate.Position> Positions => Set<AttendanceSystem.Domain.Aggregates.PositionAggregate.Position>();
    public DbSet<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee> Employees => Set<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee>();
    public DbSet<AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.DailyAttendance> DailyAttendances => Set<AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.DailyAttendance>();
    public DbSet<AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate.SystemConfiguration> SystemConfigurations => Set<AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate.SystemConfiguration>();
    public DbSet<AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog> DownloadLogs => Set<AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog>();
    public DbSet<AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert> SystemAlerts => Set<AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert>();
    public DbSet<AttendanceSystem.Domain.Aggregates.ExternalLogAggregate.ExternalAttendanceLog> ExternalAttendanceLogs => Set<AttendanceSystem.Domain.Aggregates.ExternalLogAggregate.ExternalAttendanceLog>();
    public DbSet<AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate.ShiftRoster> ShiftRosters => Set<AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate.ShiftRoster>();
    public DbSet<AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate.ExternalEmployee> ExternalEmployees => Set<AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate.ExternalEmployee>();
    public DbSet<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeAuditLog> EmployeeAuditLogs => Set<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeAuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AdmsCommand> AdmsCommands => Set<AdmsCommand>();
    public DbSet<AdmsIngestionIncident> AdmsIngestionIncidents => Set<AdmsIngestionIncident>();
    public DbSet<AttendanceAdjustment> AttendanceAdjustments => Set<AttendanceAdjustment>();
    public DbSet<AttendanceConsolidationRequest> AttendanceConsolidationRequests => Set<AttendanceConsolidationRequest>();
    public DbSet<PunchAssignment> PunchAssignments => Set<PunchAssignment>();
    public DbSet<AttendanceAdjustmentHistory> AttendanceAdjustmentHistory => Set<AttendanceAdjustmentHistory>();

    private readonly ICurrentUserService? _currentUserService;

    public AttendanceDbContext(
        DbContextOptions<AttendanceDbContext> options,
        IPublisher publisher,
        ICurrentUserService? currentUserService = null) : base(options)
    {
        _publisher = publisher;
        _currentUserService = currentUserService;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Configurar tablas de Identity

        modelBuilder.Ignore<DomainEvent>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AttendanceDbContext).Assembly);

        // Configurar conversión automática de DateTime a UTC para PostgreSQL
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                            v => v.Kind == DateTimeKind.Utc ? v : (v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc)),
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }
            }
        }
    }

    /// <summary>Identifica las reclamaciones de outbox hechas por esta instancia (ruta inmediata).</summary>
    private readonly string _outboxClaimant = $"ctx:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    /// <summary>Eventos guardados dentro de una transacción explícita que se publicarán tras su commit.</summary>
    private readonly List<(OutboxMessage Message, INotification Event)> _publishAfterCommit = new();

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        KeepUnchangedReplacedDailies();

        // 1. H24: Recolectar eventos de dominio de entidades IAggregateRoot y preparar Outbox
        var domainEntities = ChangeTracker.Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var prepared = new List<(OutboxMessage Message, INotification Event)>();
        var lockedUntil = DateTime.UtcNow.Add(OutboxStore.DefaultLease);

        foreach (var entity in domainEntities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var content = DomainEventJson.Serialize(domainEvent);
                var message = OutboxMessage.Create(domainEvent, content);
                // F04-02: nace reclamado por esta instancia; el worker no lo toma salvo que venza el lease
                // (caída antes de publicar), lo que evita la doble publicación entre ambas rutas.
                message.Lock(_outboxClaimant, lockedUntil);
                prepared.Add((message, domainEvent));
                OutboxMessages.Add(message);
            }
        }

        await AuditEmployeeChangesAsync(cancellationToken);

        // 2. Persistir agregados y Outbox en la misma transacción atómica
        int result;
        try
        {
            result = await base.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // F03-05 / F03-07: si nada se guardó, los mensajes preparados no deben persistirse en un
            // reintento sin su agregado (eventos huérfanos). Los eventos siguen en las entidades y se
            // vuelven a preparar si el agregado se guarda después.
            foreach (var (message, _) in prepared)
            {
                Entry(message).State = EntityState.Detached;
            }
            throw;
        }

        // H24: limpiar eventos sólo tras guardar, para evitar duplicación en re-saves sin perderlos ante fallos
        foreach (var entity in domainEntities)
        {
            entity.ClearDomainEvents();
        }

        if (prepared.Count == 0) return result;

        // 3. F04-01: dentro de una transacción explícita nada se publica hasta su commit; si se revierte,
        //    los mensajes desaparecen con ella y ningún efecto externo depende de datos inexistentes.
        if (Database.CurrentTransaction != null)
        {
            _publishAfterCommit.AddRange(prepared);
            return result;
        }

        await PublishPreparedAsync(prepared, cancellationToken);
        return result;
    }

    /// <summary>
    /// F07-07: el recálculo reemplaza cada jornada (borra la anterior e inserta la nueva, F02-06). Si la nueva tiene
    /// exactamente los mismos valores que la que reemplaza, se conserva la fila existente y no se escribe nada: el
    /// resultado es idéntico y los consumidores referencian la jornada por (EmployeeId, Date), no por su Id.
    /// </summary>
    private void KeepUnchangedReplacedDailies()
    {
        var deleted = ChangeTracker.Entries<DailyAttendance>()
            .Where(e => e.State == EntityState.Deleted)
            .ToDictionary(e => (e.Entity.EmployeeId, e.Entity.Date.Date));
        if (deleted.Count == 0) return;

        foreach (var added in ChangeTracker.Entries<DailyAttendance>().Where(e => e.State == EntityState.Added).ToList())
        {
            if (added.Entity.DomainEvents.Count > 0) continue;
            if (!deleted.TryGetValue((added.Entity.EmployeeId, added.Entity.Date.Date), out var existing)) continue;

            var same = added.Properties.All(p =>
                p.Metadata.IsPrimaryKey() || p.Metadata.IsConcurrencyToken ||
                Equals(p.CurrentValue, existing.Property(p.Metadata.Name).OriginalValue));
            if (!same) continue;

            added.State = EntityState.Detached;
            existing.State = EntityState.Unchanged;
        }
    }

    private async Task PublishPreparedAsync(List<(OutboxMessage Message, INotification Event)> prepared, CancellationToken cancellationToken)
    {
        await OutboxStore.PublishClaimedAsync(prepared.Select(p => (p.Message, (INotification?)p.Event)).ToList(), _publisher, cancellationToken);

        try
        {
            await base.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // Si no se puede registrar el resultado, el lease vence y OutboxBackgroundProcessor reintenta.
        }
    }

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _publishAfterCommit.Clear();
        return await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await Database.CommitTransactionAsync(cancellationToken);

        if (_publishAfterCommit.Count > 0)
        {
            var committed = _publishAfterCommit.ToList();
            _publishAfterCommit.Clear();
            await PublishPreparedAsync(committed, CancellationToken.None);
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Database.RollbackTransactionAsync(cancellationToken);
        }
        finally
        {
            DropUncommittedOutbox();
        }
    }

    public void DiscardChanges()
    {
        DropUncommittedOutbox();
        ChangeTracker.Clear();
    }

    private void DropUncommittedOutbox()
    {
        foreach (var (message, _) in _publishAfterCommit)
        {
            Entry(message).State = EntityState.Detached;
        }
        _publishAfterCommit.Clear();
    }

    private async Task AuditEmployeeChangesAsync(CancellationToken cancellationToken)
    {
        var userName = _currentUserService?.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = "Sistema";
        }
        var userId = _currentUserService?.UserId;
        var now = DateTime.UtcNow;

        var entries = ChangeTracker.Entries<Employee>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .ToList();

        if (!entries.Any()) return;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.SetCreatedAudit(now, userName);
                }

                var auditLog = EmployeeAuditLog.Create(
                    entry.Entity.Id,
                    "Alta de Empleado",
                    now,
                    userId,
                    userName,
                    $"Empleado {entry.Entity.GetFullName()} registrado en el sistema."
                );

                EmployeeAuditLogs.Add(auditLog);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.SetUpdatedAudit(now, userName);

                var changes = new List<EmployeeAuditFieldChange>();

                foreach (var prop in entry.Properties)
                {
                    if (!prop.IsModified) continue;

                    var propName = prop.Metadata.Name;
                    if (propName is nameof(Employee.UpdatedAt) or nameof(Employee.UpdatedBy) or nameof(Employee.CreatedAt) or nameof(Employee.CreatedBy))
                        continue;

                    // RestDay es la propiedad individual redundante con la lista RestDays; ignorar para evitar duplicados o falsos cambios
                    if (propName == nameof(Employee.RestDay) && entry.Entity.RestDays != null && entry.Entity.RestDays.Count > 0)
                        continue;

                    var original = prop.OriginalValue;
                    var current = prop.CurrentValue;

                    if (AreValuesEqual(original, current)) continue;

                    var fieldChange = await BuildFieldChangeAsync(propName, original, current, cancellationToken);
                    if (fieldChange != null)
                    {
                        changes.Add(fieldChange);
                    }
                }

                if (changes.Any())
                {
                    var isStatusChange = changes.Any(c => c.PropertyName == nameof(Employee.Status));
                    var action = isStatusChange ? "Cambio de Estado" : "Modificación de Perfil";

                    var summary = string.Join("; ", changes.Select(c => $"{c.DisplayName}: '{c.OldValue ?? "Ninguno"}' ➔ '{c.NewValue ?? "Ninguno"}'"));
                    if (summary.Length > 1000)
                    {
                        summary = summary.Substring(0, 997) + "...";
                    }

                    var changesJson = JsonSerializer.Serialize(changes);

                    var auditLog = EmployeeAuditLog.Create(
                        entry.Entity.Id,
                        action,
                        now,
                        userId,
                        userName,
                        summary,
                        changesJson
                    );

                    EmployeeAuditLogs.Add(auditLog);
                }
            }
        }
    }

    private async Task<EmployeeAuditFieldChange?> BuildFieldChangeAsync(
        string propertyName,
        object? original,
        object? current,
        CancellationToken cancellationToken)
    {
        string displayName;
        string? oldVal = null;
        string? newVal = null;

        switch (propertyName)
        {
            case nameof(Employee.FirstName):
                displayName = "Nombre";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.LastName):
                displayName = "Apellidos";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.Email):
                displayName = "Correo Electrónico";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.PhoneNumber):
                displayName = "Teléfono";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.HireDate):
                displayName = "Fecha de Contratación";
                oldVal = original is DateTime d1 ? d1.ToString("yyyy-MM-dd") : original?.ToString();
                newVal = current is DateTime d2 ? d2.ToString("yyyy-MM-dd") : current?.ToString();
                break;

            case nameof(Employee.Gender):
                displayName = "Género";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.Status):
                displayName = "Estado";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.BranchId):
                displayName = "Sucursal";
                oldVal = await ResolveBranchNameAsync(original, cancellationToken);
                newVal = await ResolveBranchNameAsync(current, cancellationToken);
                break;

            case nameof(Employee.DepartmentId):
                displayName = "Departamento";
                oldVal = await ResolveDepartmentNameAsync(original, cancellationToken);
                newVal = await ResolveDepartmentNameAsync(current, cancellationToken);
                break;

            case nameof(Employee.PositionId):
                displayName = "Puesto";
                oldVal = await ResolvePositionNameAsync(original, cancellationToken);
                newVal = await ResolvePositionNameAsync(current, cancellationToken);
                break;

            case nameof(Employee.ShiftType):
                displayName = "Tipo de Turno";
                oldVal = original?.ToString() ?? "Ninguno";
                newVal = current?.ToString() ?? "Ninguno";
                break;

            case nameof(Employee.ScheduleId):
                displayName = "Horario Asignado";
                oldVal = await ResolveShiftNameAsync(original, cancellationToken);
                newVal = await ResolveShiftNameAsync(current, cancellationToken);
                break;

            case nameof(Employee.RestDay):
                displayName = "Día de Descanso";
                oldVal = FormatWeekDay(original);
                newVal = FormatWeekDay(current);
                break;

            case nameof(Employee.RestDays):
                displayName = "Días de Descanso";
                oldVal = FormatWeekDays(original);
                newVal = FormatWeekDays(current);
                break;

            case nameof(Employee.OvertimeAuthorized):
                displayName = "Horas Extras Autorizadas";
                oldVal = (bool?)original == true ? "Sí" : "No";
                newVal = (bool?)current == true ? "Sí" : "No";
                break;

            case nameof(Employee.OvertimeCalculationMethod):
                displayName = "Método Cálculo HE";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.OvertimeCapType):
                displayName = "Tope Horas Extras";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.OvertimeCapMinutes):
                displayName = "Minutos Tope HE";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.CalculateOvertimeBeforeEntry):
                displayName = "Calcular HE Antes de Entrada";
                oldVal = (bool?)original == true ? "Sí" : "No";
                newVal = (bool?)current == true ? "Sí" : "No";
                break;

            case nameof(Employee.CardNumber):
                displayName = "Número de Tarjeta";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.DevicePassword):
                displayName = "Contraseña en Reloj";
                oldVal = string.IsNullOrEmpty(original?.ToString()) ? "(Sin contraseña)" : "********";
                newVal = string.IsNullOrEmpty(current?.ToString()) ? "(Sin contraseña)" : "********";
                break;

            case nameof(Employee.DevicePrivilege):
                displayName = "Privilegio en Reloj";
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;

            case nameof(Employee.Photo):
                displayName = "Fotografía";
                oldVal = string.IsNullOrEmpty(original?.ToString()) ? "(Sin foto)" : "(Foto anterior)";
                newVal = string.IsNullOrEmpty(current?.ToString()) ? "(Sin foto)" : "(Foto actualizada)";
                break;

            case nameof(Employee.FaceTemplate):
                displayName = "Template Facial";
                oldVal = string.IsNullOrEmpty(original?.ToString()) ? "(Sin rostro)" : "(Rostro registrado)";
                newVal = string.IsNullOrEmpty(current?.ToString()) ? "(Sin rostro)" : "(Rostro actualizado)";
                break;

            default:
                displayName = propertyName;
                oldVal = original?.ToString();
                newVal = current?.ToString();
                break;
        }

        var cleanOld = string.IsNullOrWhiteSpace(oldVal) ? null : oldVal.Trim();
        var cleanNew = string.IsNullOrWhiteSpace(newVal) ? null : newVal.Trim();

        if (string.Equals(cleanOld, cleanNew, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var isOldNone = cleanOld == null || cleanOld.Equals("Ninguno", StringComparison.OrdinalIgnoreCase) || cleanOld == "-";
        var isNewNone = cleanNew == null || cleanNew.Equals("Ninguno", StringComparison.OrdinalIgnoreCase) || cleanNew == "-";

        if (isOldNone && isNewNone)
        {
            return null;
        }

        return new EmployeeAuditFieldChange
        {
            PropertyName = propertyName,
            DisplayName = displayName,
            OldValue = oldVal,
            NewValue = newVal
        };
    }

    private async Task<string?> ResolveBranchNameAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null) return null;
        var id = value is BranchId bid ? bid : BranchId.From(value.ToString()!);
        var local = Branches.Local.FirstOrDefault(b => b.Id == id);
        if (local != null) return local.Name;
        try
        {
            var entity = await Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            return entity?.Name ?? id.Value.ToString();
        }
        catch
        {
            return id.Value.ToString();
        }
    }

    private async Task<string?> ResolveDepartmentNameAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null) return null;
        var id = value is DepartmentId did ? did : DepartmentId.From(value.ToString()!);
        var local = Departments.Local.FirstOrDefault(d => d.Id == id);
        if (local != null) return local.Name;
        try
        {
            var entity = await Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            return entity?.Name ?? id.Value.ToString();
        }
        catch
        {
            return id.Value.ToString();
        }
    }

    private async Task<string?> ResolvePositionNameAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null) return null;
        var id = value is PositionId pid ? pid : PositionId.From(value.ToString()!);
        var local = Positions.Local.FirstOrDefault(p => p.Id == id);
        if (local != null) return local.Name;
        try
        {
            var entity = await Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            return entity?.Name ?? id.Value.ToString();
        }
        catch
        {
            return id.Value.ToString();
        }
    }

    private async Task<string?> ResolveShiftNameAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null) return null;
        var id = value is ShiftId sid ? sid : ShiftId.From(value.ToString()!);
        var local = Shifts.Local.FirstOrDefault(s => s.Id == id);
        if (local != null) return local.Name;
        try
        {
            var entity = await Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            return entity?.Name ?? id.Value.ToString();
        }
        catch
        {
            return id.Value.ToString();
        }
    }

    private static string FormatWeekDay(object? value) => value switch
    {
        WeekDay wd => wd switch
        {
            WeekDay.Domingo => "Domingo",
            WeekDay.Lunes => "Lunes",
            WeekDay.Martes => "Martes",
            WeekDay.Miercoles => "Miércoles",
            WeekDay.Jueves => "Jueves",
            WeekDay.Viernes => "Viernes",
            WeekDay.Sabado => "Sábado",
            _ => wd.ToString()
        },
        int i => i switch
        {
            0 => "Domingo",
            1 => "Lunes",
            2 => "Martes",
            3 => "Miércoles",
            4 => "Jueves",
            5 => "Viernes",
            6 => "Sábado",
            _ => i.ToString()
        },
        _ => value?.ToString() ?? "Ninguno"
    };

    private static string FormatWeekDays(object? value)
    {
        if (value is IEnumerable<WeekDay> days)
        {
            var list = days.OrderBy(d => (int)d).Select(d => FormatWeekDay(d)).ToList();
            return list.Any() ? string.Join(", ", list) : "Ninguno";
        }
        return value?.ToString() ?? "Ninguno";
    }

    private static bool AreValuesEqual(object? v1, object? v2)
    {
        if (ReferenceEquals(v1, v2)) return true;
        if (v1 is null && v2 is null) return true;
        if (v1 is null || v2 is null) return false;

        if (v1 is IEnumerable<WeekDay> wdList1 && v2 is IEnumerable<WeekDay> wdList2)
        {
            var l1 = wdList1.OrderBy(x => (int)x).ToList();
            var l2 = wdList2.OrderBy(x => (int)x).ToList();
            return l1.SequenceEqual(l2);
        }

        if (v1 is string s1 && v2 is string s2)
        {
            return string.Equals(s1.Trim(), s2.Trim(), StringComparison.Ordinal);
        }

        if (v1 is DateTime dt1 && v2 is DateTime dt2)
        {
            return dt1.Equals(dt2);
        }

        return Equals(v1, v2);
    }
}
