using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// F07-04 / F03-04: los empleados se recalculan por lotes en una transacción; si el lote falla, se reprocesa
/// empleado por empleado y un fallo sólo revierte su propia unidad.
/// </summary>
public class BatchRecalculationTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);

    private static Employee Create(string id, AttendanceSystem.Domain.Aggregates.ShiftAggregate.Shift shift) =>
        Employee.Create(EmployeeId.From(id), "Empleado", id, null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: Array.Empty<WeekDay>());

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, Employee e, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(e.Id, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    [Fact]
    public async Task AFailingEmployee_OnlyRevertsItsOwnUnit_AndTheRestOfTheBatchIsConfirmed()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        var first = Create("EMP-BAT-1", shift);
        var broken = Create("EMP-BAT-2", shift);
        var third = Create("EMP-BAT-3", shift);
        h.Store.Employees.AddRange(new[] { first, broken, third });
        AttendanceRecord? foreign = null;
        foreach (var e in new[] { first, broken, third })
        {
            var entry = Punch(h, e, Monday.AddHours(8));
            Punch(h, e, Monday.AddHours(16));
            if (e == first) foreign = entry;
        }
        // Decisión corrupta (sin pasar por el comando): la jornada de EMP-BAT-2 referencia un marcaje de EMP-BAT-1.
        var corrupt = AttendanceAdjustment.Create(broken.Id, Monday, "migracion");
        corrupt.AssignPosition(AttendancePosition.CheckIn, foreign!.Id, foreign.CheckTime, "migracion");
        h.Store.Adjustments.Add(corrupt);

        var act = () => h.RecalculateAsync(Monday, Monday);

        var failure = (await act.Should().ThrowAsync<AttendanceRecalculationException>()).Which;
        failure.TotalUnits.Should().Be(3);
        failure.SuccessfulUnits.Should().Be(2);
        failure.FailedUnits.Should().ContainSingle(f => f.EmployeeId == broken.Id.Value);
        h.Store.Dailies.Select(d => d.EmployeeId).Should().BeEquivalentTo(new[] { first.Id, third.Id },
            "los empleados sanos del lote se confirmaron; el fallido no dejó nada");
        h.Store.Dailies.Single(d => d.EmployeeId == first.Id).CheckInRecordId.Should().Be(foreign.Id);
    }
}
