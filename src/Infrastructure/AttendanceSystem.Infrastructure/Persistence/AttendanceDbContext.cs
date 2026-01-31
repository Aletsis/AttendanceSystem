using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Entities;

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



    public AttendanceDbContext(
        DbContextOptions<AttendanceDbContext> options,
        IPublisher publisher) : base(options)
    {
        _publisher = publisher;
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
                            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }
            }
        }
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        // Publicar eventos de dominio antes de guardar
        var domainEntities = ChangeTracker.Entries()
            .Where(e => e.Entity.GetType().BaseType != null && 
                       e.Entity.GetType().BaseType.IsGenericType &&
                       e.Entity.GetType().BaseType.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = new List<DomainEvent>();
        foreach (var entity in domainEntities)
        {
            var eventsProperty = entity.GetType().GetProperty("DomainEvents");
            if (eventsProperty != null)
            {
                var events = eventsProperty.GetValue(entity) as IEnumerable<DomainEvent>;
                if (events != null)
                {
                    domainEvents.AddRange(events);
                }
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in domainEvents)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }

        return result;
    }
}
