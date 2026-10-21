using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// La lista paginada de empleados se traduce a SQL en PostgreSQL (con el proveedor en memoria e.Id.Value se
/// evaluaba en el cliente y la falla no se veía: la pantalla de empleados quedaba vacía).
/// </summary>
public class PostgresEmployeeQueryTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresEmployeeQueryTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Pagination_SearchesAndSortsById_InTheDatabase()
    {
        if (!_fixture.IsAvailable) return;

        var branch = Branch.Create("P01", "Sucursal paginación", null);
        var department = Department.Create("Depto paginación", null);
        var position = Position.Create("Puesto paginación", null, 1000m);
        department.AddPosition(position);
        var ids = new[] { "PG910", "PG92", "PG9100", "PG93" };
        await using (var context = _fixture.CreateDbContext())
        {
            context.Branches.Add(branch);
            context.Departments.Add(department);
            context.Positions.Add(position);
            context.Employees.AddRange(ids.Select(id => Employee.Create(EmployeeId.From(id), "Ana", "Paginación " + id, null, null,
                new DateTime(2024, 1, 1), Gender.Female, branch.Id, department.Id, position.Id, ShiftType.Matutino)));
            await context.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var repository = new EmployeeRepository(verify);

        var (sorted, total) = await repository.GetPaginatedAsync(0, 10, "PG9", null, 0);
        total.Should().Be(4, "la búsqueda por ID se traduce a SQL");
        sorted.Select(e => e.Id.Value).Should().Equal(new[] { "PG92", "PG93", "PG910", "PG9100" }, "orden natural: longitud y luego valor");

        var (descending, _) = await repository.GetPaginatedAsync(0, 10, null, "ID", 2, idFilter: "PG9");
        descending.Select(e => e.Id.Value).Should().Equal(new[] { "PG9100", "PG910", "PG93", "PG92" });
    }
}
