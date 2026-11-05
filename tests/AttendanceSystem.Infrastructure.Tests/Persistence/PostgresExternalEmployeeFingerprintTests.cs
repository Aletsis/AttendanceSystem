using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>Huellas de empleados externos: se guardan, se reemplazan por dedo sin duplicar filas y se borran con el empleado.</summary>
public class PostgresExternalEmployeeFingerprintTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresExternalEmployeeFingerprintTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fingerprints_RoundTrip_AreReplacedByFinger_AndAreDeletedWithTheEmployee()
    {
        if (!_fixture.IsAvailable) return;

        var branchId = BranchId.CreateNew();
        var employee = ExternalEmployee.Create(branchId, "FP" + Guid.NewGuid().ToString("N")[..6], "Ana", "López");
        employee.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(0, "TPL-0"), new ExternalEmployeeFingerprint(6, "TPL-6") });
        await using (var context = _fixture.CreateDbContext())
        {
            new ExternalEmployeeRepository(context).Add(employee);
            await context.SaveChangesAsync();
        }

        // Otra lectura del reloj trae el dedo 6 re-enrolado: se actualiza en un contexto nuevo, como en la importación.
        await using (var context = _fixture.CreateDbContext())
        {
            var repository = new ExternalEmployeeRepository(context);
            var loaded = await repository.GetByBranchAndEmployeeNumberAsync(branchId, employee.EmployeeNumber);
            loaded!.Fingerprints.Should().HaveCount(2);
            loaded.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(6, "TPL-6b") });
            repository.Update(loaded);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var repository = new ExternalEmployeeRepository(context);
            var loaded = await repository.GetByIdAsync(employee.Id);
            loaded!.Fingerprints.Select(f => (f.FingerIndex, f.Template))
                .Should().BeEquivalentTo(new[] { (0, "TPL-0"), (6, "TPL-6b") });

            repository.Delete(loaded);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        {
            (await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM \"ExternalEmployeeFingerprints\" WHERE \"ExternalEmployeeId\" = {0}", employee.Id)
                .SingleAsync())
                .Should().Be(0);
        }
    }
}
