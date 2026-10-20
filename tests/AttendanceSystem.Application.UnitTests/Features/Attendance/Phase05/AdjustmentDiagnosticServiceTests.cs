using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase05;

/// <summary>F05-08: clasificación revisable de decisiones heredadas, sin inferir intención.</summary>
public class AdjustmentDiagnosticServiceTests
{
    private static readonly DateTime Day = new(2026, 8, 4);

    [Fact]
    public async Task Diagnose_SeparatesCertainDecisionsFromThoseWithoutEvidence()
    {
        var h = new AttendanceRecalculationHarness();
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var e1 = EmployeeId.From("EMP-DG-1");
        var e2 = EmployeeId.From("EMP-DG-2");
        var e3 = EmployeeId.From("EMP-DG-3");
        var e4 = EmployeeId.From("EMP-DG-4");
        var e5 = EmployeeId.From("EMP-DG-5");
        var e6 = EmployeeId.From("EMP-DG-6");

        // 1. Decisión válida y explícita.
        var own = AttendanceRegressionFixtures.CreatePunch(e1, Day.AddHours(8), CheckType.CheckIn);
        h.Store.Records.Add(own);
        var valid = AttendanceAdjustment.Create(e1, Day, "rh");
        valid.SetCheckIn(own.Id, own.CheckTime, "rh");
        valid.SetRestDayOverride(false, "rh");

        // 2. Marcaje ajeno.
        var foreign = AttendanceRegressionFixtures.CreatePunch(e6, Day.AddHours(8), CheckType.CheckIn);
        h.Store.Records.Add(foreign);
        var invalid = AttendanceAdjustment.Create(e2, Day, "rh");
        invalid.SetCheckIn(foreign.Id, foreign.CheckTime, "rh");

        // 3. Desasignación antigua: el ajuste quedó sin ninguna decisión.
        var empty = AttendanceAdjustment.Create(e3, Day, "Usuario RH");
        empty.SetShiftOverride(shift.Id, "Usuario RH");
        empty.ClearShiftOverride("Usuario RH");

        // 4. Ajuste derivado de un resultado calculado.
        var derived = AttendanceAdjustment.Create(e4, Day, "Migración F05");
        derived.SetRestDayOverride(true, "Migración F05", "Preservado de DailyAttendance");

        // 5. Turno inexistente.
        var missingShift = AttendanceAdjustment.Create(e5, Day, "rh");
        missingShift.SetShiftOverride(ShiftId.CreateNew(), "rh");

        h.Store.Adjustments.AddRange(new[] { valid, invalid, empty, derived, missingShift });

        // 6. Jornada con ajustes manuales y sin decisión guardada.
        var lost = DailyAttendance.Create(e6, Day, shift, null, null, false);
        lost.SetRestDayOverride(true);
        h.Store.Dailies.Add(lost);

        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { shift });
        var service = new AdjustmentDiagnosticService(h.AdjustmentRepository, h.DailyRepository, h.AttendanceRepository, shifts.Object);

        var report = await service.DiagnoseAsync(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day));

        report.AdjustmentsInspected.Should().Be(5);
        report.Preservable.Should().Be(1);
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.Preservable && f.EmployeeId == e1.Value);
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.InvalidPunchReference && f.EmployeeId == e2.Value && f.Detail.Contains("pertenece a EMP-DG-6"));
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.NoEvidenceOfIntent && f.EmployeeId == e3.Value);
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.DerivedFromCalculatedResult && f.EmployeeId == e4.Value);
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.MissingShift && f.EmployeeId == e5.Value);
        report.Findings.Should().Contain(f => f.Kind == AdjustmentFindingKind.LostDecision && f.EmployeeId == e6.Value);

        // Sólo diagnostica: no crea, modifica ni completa decisiones.
        h.Store.Adjustments.Should().HaveCount(5);
        h.Store.SaveChangesCount.Should().Be(0);
        empty.Revision.Should().Be(2);
    }
}
