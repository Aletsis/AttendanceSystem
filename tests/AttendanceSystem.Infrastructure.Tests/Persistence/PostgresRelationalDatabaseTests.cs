using System;
using System.Threading.Tasks;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Verificaciones relacionales contra PostgreSQL real (F00-03).
/// Valida que una base de datos vacía se crea desde migraciones reales,
/// ejecuta operaciones relacionales y aplica transacciones sin servicios productivos.
/// </summary>
public class PostgresRelationalDatabaseTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresRelationalDatabaseTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Postgres_Database_Should_Apply_All_Migrations_And_Have_Required_Tables()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();

        // 1. Validar que no hay migraciones pendientes
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        pendingMigrations.Should().BeEmpty("todas las migraciones deben aplicarse satisfactoriamente en una base nueva");

        // 2. Validar que las tablas principales de cálculo y auditoría existen en PostgreSQL
        var canQueryRecords = await context.AttendanceRecords.AnyAsync();
        var canQueryDaily = await context.DailyAttendances.AnyAsync();
        var canQueryAdjustments = await context.AttendanceAdjustments.AnyAsync();
        var canQueryConsolidation = await context.AttendanceConsolidationRequests.AnyAsync();

        // No lanzaron excepción relacional, por lo que las tablas existen
        canQueryRecords.Should().BeFalse();
        canQueryDaily.Should().BeFalse();
        canQueryAdjustments.Should().BeFalse();
        canQueryConsolidation.Should().BeFalse();
    }

    [Fact]
    public async Task Postgres_Transaction_Rollback_Should_Not_Leave_Orphan_Records()
    {
        if (!_fixture.IsAvailable) return;

        var employeeId = EmployeeId.From("EMP-TEST-001");
        var deviceId = DeviceId.From("DEV-POSTGRES-01");
        var checkTime = DateTime.UtcNow;

        // 1. Iniciar transacción e insertar registro, luego forzar rollback
        await using (var context = _fixture.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            var record = AttendanceRecord.Create(
                employeeId,
                deviceId,
                checkTime,
                VerifyMethod.Fingerprint,
                CheckType.CheckIn,
                originalCheckTime: checkTime,
                timeZoneId: "America/Mexico_City",
                source: "PostgresRelationalTest");

            await context.AttendanceRecords.AddAsync(record);
            await context.SaveChangesAsync();

            // Rollback intencional sin commit
            await transaction.RollbackAsync();
        }

        // 2. Verificar que en una conexión separada el registro NO existe
        await using (var verifyContext = _fixture.CreateDbContext())
        {
            var persistedRecord = await verifyContext.AttendanceRecords
                .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.CheckTime == checkTime);

            persistedRecord.Should().BeNull("la transacción con rollback en PostgreSQL no debe dejar registros huérfanos");
        }
    }
}
