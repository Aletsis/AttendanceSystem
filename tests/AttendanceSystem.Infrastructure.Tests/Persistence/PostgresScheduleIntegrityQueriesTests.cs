using System.Threading.Tasks;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Caso 10 sobre PostgreSQL: las consultas que impiden borrar un horario en uso (vigencias del empleado, roster y
/// ajustes) y la tabla de patrones rotativos se traducen y existen tras las migraciones.
/// </summary>
public class PostgresScheduleIntegrityQueriesTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresScheduleIntegrityQueriesTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ShiftInUseQueries_AndRotationPatterns_RunOnPostgres()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var unused = ShiftId.CreateNew();

        (await new EmployeeRepository(context).IsShiftInUseAsync(unused)).Should().BeFalse();
        (await new ShiftRosterRepository(context).CountByShiftAsync(unused)).Should().Be(0);
        (await new AttendanceAdjustmentRepository(context).CountByShiftOverrideAsync(unused)).Should().Be(0);
        (await new RotationPatternRepository(context).GetIndefiniteAsync()).Should().BeEmpty();
        (await new RotationPatternRepository(context).GetByEmployeeAsync(EmployeeId.From("EMP-NONE"))).Should().BeNull();
    }
}
