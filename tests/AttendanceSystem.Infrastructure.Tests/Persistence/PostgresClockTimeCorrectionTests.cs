using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Devices.Quarantine;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Common;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Reloj en Quintana Roo (UTC-5): marcajes con hora imposible van a cuarentena y se liberan con la hora corregida; la
/// corrección por rango reinterpreta los marcajes del reloj sin violar la unicidad (empleado, instante).
/// </summary>
public class PostgresClockTimeCorrectionTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Zone = "America/Cancun";
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresClockTimeCorrectionTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    private static IAuthoritativeTimeZoneResolver Resolver()
    {
        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Zone);
        return resolver.Object;
    }

    private static PunchQuarantineService Quarantine(AttendanceDbContext context) =>
        new(new QuarantinedPunchRepository(context), new UnitOfWork(context), DefaultTimeNormalizer.Instance, new SystemAlertRepository(context));

    private async Task<Device> DeviceAsync(string name)
    {
        var device = Device.Create(Guid.NewGuid().ToString(), name, $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}", 4370, timeZoneId: Zone);
        await using var context = _fixture.CreateDbContext();
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device;
    }

    [Fact]
    public async Task ImpossiblePunches_GoToQuarantineOnce_AndAreReleasedWithTheCorrectedTime()
    {
        if (!_fixture.IsAvailable) return;
        var device = await DeviceAsync("Reloj cuarentena");
        var clockNow = DefaultTimeNormalizer.Instance.ToCivilTime(DateTime.UtcNow, Zone);
        var future = new DateTime(clockNow.Year, clockNow.Month, clockNow.Day, clockNow.Hour, clockNow.Minute, 0).AddHours(3);
        var logs = new List<AttendanceLogDto>
        {
            new("Q-EMP-1", new DateTime(2026, 10, 1, 9, 0, 0), 1, 0, Source: "ADMS"),
            new("Q-EMP-1", future, 1, 1, Source: "ADMS"),
            new("Q-EMP-2", new DateTime(1999, 12, 31, 23, 0, 0), 1, 0, Source: "ADMS"),
        };

        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var context = _fixture.CreateDbContext();
            var handler = new RecordAttendanceBatchCommandHandler(new AttendanceRepository(context), new UnitOfWork(context), DefaultTimeNormalizer.Instance,
                Resolver(), NullLogger<RecordAttendanceBatchCommandHandler>.Instance, quarantine: Quarantine(context));
            var result = await handler.Handle(new RecordAttendanceBatchCommand(device.Id.Value, logs), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : null);
        }

        await using (var verify = _fixture.CreateDbContext())
        {
            (await verify.AttendanceRecords.CountAsync(r => r.DeviceId == device.Id)).Should().Be(1, "sólo el marcaje plausible se ingresa");
            var quarantined = await verify.QuarantinedPunches.Where(q => q.DeviceId == device.Id).ToListAsync();
            quarantined.Should().HaveCount(2, "reenviar el lote no duplica la cuarentena");
            quarantined.Should().OnlyContain(q => q.Status == QuarantineStatus.Pending && q.TimeZoneId == Zone);
            (await verify.SystemAlerts.CountAsync(a => a.Type == AlertType.PunchQuarantined && a.ReferenceId == device.Id.Value)).Should().BeGreaterThan(0);
        }

        Guid futureId, oldId;
        await using (var read = _fixture.CreateDbContext())
        {
            futureId = (await read.QuarantinedPunches.SingleAsync(q => q.DeviceId == device.Id && q.EmployeeId == "Q-EMP-1")).Id;
            oldId = (await read.QuarantinedPunches.SingleAsync(q => q.DeviceId == device.Id && q.EmployeeId == "Q-EMP-2")).Id;
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var decisions = new QuarantinedPunchDecisionHandler(new QuarantinedPunchRepository(context), new AttendanceRepository(context), new UnitOfWork(context),
                DefaultTimeNormalizer.Instance, Quarantine(context));

            var stillFuture = await decisions.Handle(new ReleaseQuarantinedPunchCommand(futureId, -60, "El reloj iba 3 h adelantado"), CancellationToken.None);
            stillFuture.IsFailure.Should().BeTrue("con 1 h de corrección sigue en el futuro");

            var released = await decisions.Handle(new ReleaseQuarantinedPunchCommand(futureId, -180, "El reloj iba 3 h adelantado"), CancellationToken.None);
            released.IsSuccess.Should().BeTrue(released.IsFailure ? released.Error : null);

            (await decisions.Handle(new DiscardQuarantinedPunchCommand(oldId, "Reloj sin fecha tras cambio de pila"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using (var verify = _fixture.CreateDbContext())
        {
            var record = (await verify.AttendanceRecords.Where(r => r.DeviceId == device.Id && r.EmployeeId == EmployeeId.From("Q-EMP-1")).ToListAsync())
                .Single(r => DateTime.SpecifyKind(r.OriginalCheckTime!.Value, DateTimeKind.Unspecified) != new DateTime(2026, 10, 1, 9, 0, 0));
            DateTime.SpecifyKind(record.OriginalCheckTime!.Value, DateTimeKind.Unspecified).Should().Be(future.AddHours(-3));
            record.CheckTime.Should().Be(DateTime.SpecifyKind(future.AddHours(2), DateTimeKind.Utc), "hora corregida en UTC-5");
            var punches = await verify.QuarantinedPunches.Where(q => q.DeviceId == device.Id).ToListAsync();
            punches.Single(q => q.Id == futureId).Status.Should().Be(QuarantineStatus.Released);
            punches.Single(q => q.Id == futureId).ReleasedRecordId.Should().Be(record.Id);
            punches.Single(q => q.Id == oldId).Status.Should().Be(QuarantineStatus.Discarded);
        }
    }

    [Fact]
    public async Task Correction_ShiftsConsecutivePunches_SkipsCollisions_AndRequestsRecalculation()
    {
        if (!_fixture.IsAvailable) return;
        var device = await DeviceAsync("Reloj desfasado");
        var other = await DeviceAsync("Otro reloj");
        var day = new DateTime(2026, 9, 14);
        var ana = EmployeeId.From("C-ANA");
        var beto = EmployeeId.From("C-BETO");
        AttendanceRecord Punch(EmployeeId employee, Device on, int hour) =>
            AttendanceRecord.Create(employee, on.Id, DateTime.SpecifyKind(day.AddHours(hour + 5), DateTimeKind.Utc), VerifyMethod.Fingerprint, CheckType.CheckIn,
                workDate: DateOnly.FromDateTime(day), originalCheckTime: day.AddHours(hour), timeZoneId: Zone, source: "ZKTeco");

        var anaPunches = new[] { Punch(ana, device, 8), Punch(ana, device, 9), Punch(ana, device, 10) };
        var betoPunch = Punch(beto, device, 8);
        await using (var context = _fixture.CreateDbContext())
        {
            context.AttendanceRecords.AddRange(anaPunches);
            context.AttendanceRecords.Add(betoPunch);
            context.AttendanceRecords.Add(Punch(beto, other, 9)); // ocupa el instante al que se movería el de Beto
            context.PunchAssignments.Add(PunchAssignment.Create(anaPunches[0].Id, ana, DateOnly.FromDateTime(day), PunchAssignmentRole.CheckIn, PunchAssignmentOrigin.Manual));
            await context.SaveChangesAsync();
        }

        CorrectDevicePunchTimesCommand Command(bool apply, int offset) =>
            new(device.Id.Value, DateOnly.FromDateTime(day), DateOnly.FromDateTime(day), null, offset, "El reloj quedó con la hora de UTC-6", apply);

        async Task<PunchCorrectionResultDto> RunAsync(bool apply, int offset = 60)
        {
            await using var context = _fixture.CreateDbContext();
            var handler = new CorrectDevicePunchTimesCommandHandler(new DeviceRepository(context), new PunchTimeCorrectionRepository(context),
                new AttendanceRepository(context), new PunchAssignmentRepository(context), new AttendanceConsolidationRepository(context),
                NoOpConsolidationWakeup.Instance, new UnitOfWork(context),
                new PostgresAttendanceCalculationLockService(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance),
                DefaultTimeNormalizer.Instance, Resolver());
            var result = await handler.Handle(Command(apply, offset), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : null);
            return result.Value;
        }

        var preview = await RunAsync(apply: false);
        preview.Corrected.Should().Be(3);
        preview.Rows.Single(r => r.EmployeeId == beto.Value).SkipReason.Should().NotBeNull("chocaría con su marcaje de las 09:00 en otro reloj");
        await using (var unchanged = _fixture.CreateDbContext())
        {
            (await unchanged.AttendanceRecords.SingleAsync(r => r.Id == anaPunches[0].Id)).CheckTime.Should().Be(anaPunches[0].CheckTime, "la vista previa no cambia nada");
        }

        var applied = await RunAsync(apply: true);
        applied.Applied.Should().BeTrue();

        await using var verify = _fixture.CreateDbContext();
        var anaNow = await verify.AttendanceRecords.Where(r => r.EmployeeId == ana).OrderBy(r => r.CheckTime).ToListAsync();
        anaNow.Select(r => DateTime.SpecifyKind(r.OriginalCheckTime!.Value, DateTimeKind.Unspecified).Hour).Should().Equal(9, 10, 11);
        anaNow.Select(r => r.CheckTime.Hour).Should().Equal(14, 15, 16);
        (await verify.AttendanceRecords.SingleAsync(r => r.Id == betoPunch.Id)).CheckTime.Should().Be(betoPunch.CheckTime, "el choque se omite");
        (await verify.PunchAssignments.AnyAsync(a => a.Id == anaPunches[0].Id)).Should().BeFalse("la asignación del marcaje corregido se rehace al recalcular");

        var correction = await verify.PunchTimeCorrections.Include(c => c.Entries).SingleAsync(c => c.Id == applied.CorrectionId);
        correction.Entries.Should().HaveCount(3);
        correction.SkippedCount.Should().Be(1);
        correction.Entries.Should().OnlyContain(e => e.NewCheckTimeUtc - e.OldCheckTimeUtc == TimeSpan.FromHours(1));
        (await verify.AttendanceConsolidationRequests.AnyAsync(r => r.EmployeeId == ana)).Should().BeTrue();

        // Revertir en sentido contrario: cada marcaje ocupa el instante que deja el anterior.
        (await RunAsync(apply: true, offset: -60)).Corrected.Should().Be(4, "también se mueve el de Beto: a las 07:00 no choca");
        await using var reverted = _fixture.CreateDbContext();
        (await reverted.AttendanceRecords.Where(r => r.EmployeeId == ana).OrderBy(r => r.CheckTime).Select(r => r.CheckTime).ToListAsync())
            .Should().Equal(anaPunches.Select(p => p.CheckTime));
    }
}
