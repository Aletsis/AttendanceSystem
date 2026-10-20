using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Fase 02 sobre PostgreSQL real: restricciones de PunchAssignments (F02-02) y el orquestador de
/// recálculo con repositorios EF reales, incluido el rollback de una unidad inválida (verificable 5).
/// </summary>
public class PostgresPunchAssignmentTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Zone = "America/Mexico_City";
    private static readonly TimeSpan UtcMinus6 = TimeSpan.FromHours(-6);

    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresPunchAssignmentTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static AttendanceRecord Punch(EmployeeId employeeId, DateTime civil) =>
        AttendanceRecord.Create(
            employeeId,
            DeviceId.From("DEV-PG-02"),
            DateTime.SpecifyKind(civil - UtcMinus6, DateTimeKind.Utc),
            VerifyMethod.Fingerprint,
            CheckType.CheckIn,
            workDate: DateOnly.FromDateTime(civil),
            originalCheckTime: civil,
            timeZoneId: Zone,
            source: "PostgresPhase02");

    [Fact]
    public async Task CompositeForeignKey_RejectsAssigningAPunchToAnotherEmployee()
    {
        if (!_fixture.IsAvailable) return;

        var owner = EmployeeId.From("EMP-PG-FK-A");
        var record = Punch(owner, new DateTime(2026, 7, 6, 8, 0, 0));
        await using (var context = _fixture.CreateDbContext())
        {
            context.AttendanceRecords.Add(record);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        {
            context.PunchAssignments.Add(PunchAssignment.Create(record.Id, EmployeeId.From("EMP-PG-FK-B"), new DateOnly(2026, 7, 6),
                PunchAssignmentRole.CheckIn, PunchAssignmentOrigin.Manual));

            var act = () => context.SaveChangesAsync();
            (await act.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException!.Message.Should().Contain("FK_PunchAssignments_AttendanceRecords_Owner");
        }
    }

    [Fact]
    public async Task PrimaryKey_RejectsASecondJornadaForTheSamePunch()
    {
        if (!_fixture.IsAvailable) return;

        var employee = EmployeeId.From("EMP-PG-PK-A");
        var record = Punch(employee, new DateTime(2026, 7, 7, 6, 0, 0));
        await using (var context = _fixture.CreateDbContext())
        {
            context.AttendanceRecords.Add(record);
            context.PunchAssignments.Add(PunchAssignment.Create(record.Id, employee, new DateOnly(2026, 7, 6),
                PunchAssignmentRole.CheckOut, PunchAssignmentOrigin.Automatic));
            await context.SaveChangesAsync();
        }

        // Inserción directa (sin rastreo de EF) de una segunda asignación del mismo marcaje.
        await using (var context = _fixture.CreateDbContext())
        {
            var act = () => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"PunchAssignments\" (\"AttendanceRecordId\", \"EmployeeId\", \"WorkDate\", \"Role\", \"Origin\", \"AssignedAt\") VALUES ({record.Id.Value}, {employee.Value}, {new DateOnly(2026, 7, 7)}, 1, 1, {DateTime.UtcNow})");
            (await act.Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.ConstraintName.Should().Be("PK_PunchAssignments");
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var invalidRole = () => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"PunchAssignments\" SET \"Role\" = 99 WHERE \"AttendanceRecordId\" = {record.Id.Value}");
            (await invalidRole.Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.ConstraintName.Should().Be("CK_PunchAssignments_Role");
        }
    }

    [Fact]
    public async Task Recalculation_OnPostgres_KeepsNightExit_PersistsAssignments_AndRollsBackInvalidUnits()
    {
        if (!_fixture.IsAvailable) return;

        var employeeId = EmployeeId.From("EMP-PG-ROT-1");
        var monday = new DateTime(2026, 7, 13);
        var tuesday = monday.AddDays(1);
        var night = Shift.Create("Nocturno 22-06", new TimeSpan(22, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Nocturno);
        var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var employee = Employee.Create(employeeId, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Rotativo, null, restDays: Array.Empty<WeekDay>());

        var inMonday = Punch(employeeId, monday.AddHours(22));
        var exitMonday = Punch(employeeId, tuesday.AddHours(6));
        var inTuesday = Punch(employeeId, tuesday.AddHours(8));
        var outTuesday = Punch(employeeId, tuesday.AddHours(16));

        await using (var context = _fixture.CreateDbContext())
        {
            context.AttendanceRecords.AddRange(inMonday, exitMonday, inTuesday, outTuesday);
            await context.SaveChangesAsync();
        }

        // ShiftRosters tiene FK a Employees; el roster se simula para no sembrar el catálogo completo.
        var rosters = new List<ShiftRoster>
        {
            ShiftRoster.Create(employeeId, monday, night.Id, isRestDay: false),
            ShiftRoster.Create(employeeId, tuesday, regular.Id, isRestDay: false)
        };

        await RecalculateAsync(employee, new[] { night, regular }, rosters, monday, tuesday);
        await RecalculateAsync(employee, new[] { night, regular }, rosters, tuesday, tuesday);

        await using (var context = _fixture.CreateDbContext())
        {
            var dailies = await context.DailyAttendances.Where(d => d.EmployeeId == employeeId).OrderBy(d => d.Date).ToListAsync();
            dailies.Should().HaveCount(2);
            dailies[0].CheckOutRecordId.Should().Be(exitMonday.Id, "la salida del lunes no se convierte en entrada del martes");
            dailies[1].CheckInRecordId.Should().Be(inTuesday.Id);

            var rows = await context.PunchAssignments.Where(a => a.EmployeeId == employeeId).ToListAsync();
            rows.Should().HaveCount(4);
            rows.Single(a => a.Id == exitMonday.Id).WorkDate.Should().Be(DateOnly.FromDateTime(monday));

            var statuses = await context.AttendanceRecords.Where(r => r.EmployeeId == employeeId).Select(r => r.Status).ToListAsync();
            statuses.Should().OnlyContain(s => s == AttendanceStatus.Processed);
        }

        // Ajuste heredado inválido: el mismo marcaje decidido manualmente en dos jornadas.
        await using (var context = _fixture.CreateDbContext())
        {
            var mondayAdj = AttendanceAdjustment.Create(employeeId, monday, "Legado");
            mondayAdj.SetCheckOut(exitMonday.Id, exitMonday.CheckTime, "Legado");
            var tuesdayAdj = AttendanceAdjustment.Create(employeeId, tuesday, "Legado");
            tuesdayAdj.SetCheckIn(exitMonday.Id, exitMonday.CheckTime, "Legado");
            context.AttendanceAdjustments.AddRange(mondayAdj, tuesdayAdj);
            await context.SaveChangesAsync();
        }

        List<Guid> dailyIdsBefore;
        await using (var context = _fixture.CreateDbContext())
        {
            dailyIdsBefore = await context.DailyAttendances.Where(d => d.EmployeeId == employeeId).OrderBy(d => d.Date).Select(d => d.Id.Value).ToListAsync();
        }

        var act = () => RecalculateAsync(employee, new[] { night, regular }, rosters, monday, tuesday);
        await act.Should().ThrowAsync<PunchAssignmentInvariantException>();

        await using (var context = _fixture.CreateDbContext())
        {
            var dailyIdsAfter = await context.DailyAttendances.Where(d => d.EmployeeId == employeeId).OrderBy(d => d.Date).Select(d => d.Id.Value).ToListAsync();
            dailyIdsAfter.Should().Equal(dailyIdsBefore, "el rollback real de PostgreSQL no deja jornadas reemplazadas a medias");
            (await context.PunchAssignments.CountAsync(a => a.EmployeeId == employeeId)).Should().Be(4);
        }
    }

    private async Task RecalculateAsync(Employee employee, IReadOnlyList<Shift> shifts, IReadOnlyList<ShiftRoster> rosters, DateTime start, DateTime end)
    {
        await using var context = _fixture.CreateDbContext();
        var dailyRepo = new DailyAttendanceRepository(context);
        var attendanceRepo = new AttendanceRepository(context);

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { employee });
        var shiftRepo = new Mock<IShiftRepository>();
        shiftRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(shifts);
        var rosterRepo = new Mock<IShiftRosterRepository>();
        rosterRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime s, DateTime e, BranchId? _, EmployeeId? _, CancellationToken _) =>
                rosters.Where(r => r.Date.Date >= s.Date && r.Date.Date <= e.Date).ToList());
        var configRepo = new Mock<ISystemConfigurationRepository>();
        configRepo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync((SystemConfiguration?)null);

        var handler = new ProcessDailyAttendanceCommandHandler(
            dailyRepo,
            attendanceRepo,
            employeeRepo.Object,
            shiftRepo.Object,
            rosterRepo.Object,
            configRepo.Object,
            new AttendanceAdjustmentRepository(context),
            new AttendanceConsolidationRepository(context),
            new PunchAssignmentRepository(context),
            new PostgresAttendanceCalculationLockService(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance),
            context,
            new SubCommandSender(dailyRepo, attendanceRepo),
            NullLogger<ProcessDailyAttendanceCommandHandler>.Instance);

        await handler.Handle(new ProcessDailyAttendanceCommand(start, end, EmployeeId: employee.Id), CancellationToken.None);
    }

    private sealed class SubCommandSender : ISender
    {
        private readonly IDailyAttendanceRepository _dailyRepo;
        private readonly IAttendanceRepository _attendanceRepo;

        public SubCommandSender(IDailyAttendanceRepository dailyRepo, IAttendanceRepository attendanceRepo)
        {
            _dailyRepo = dailyRepo;
            _attendanceRepo = attendanceRepo;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            request switch
            {
                ProcessRegularAttendanceCommand c => new ProcessRegularAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessRegularAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
                ProcessNightlyAttendanceCommand c => new ProcessNightlyAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessNightlyAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
