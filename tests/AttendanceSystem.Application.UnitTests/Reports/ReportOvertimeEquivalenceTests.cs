using AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Reports;

/// <summary>
/// F06-09: los reportes muestran las extras que guardó el motor; no vuelven a topar ni a redondear con la
/// configuración actual del empleado.
/// </summary>
public class ReportOvertimeEquivalenceTests
{
    private readonly Shift _shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);

    private Employee CreateEmployee() =>
        Employee.Create(EmployeeId.From("EMP-EQ-1"), "Ana", "Equivalencia", null, null, new DateTime(2026, 1, 1), Gender.Female,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino, _shift.Id,
            overtimeAuthorized: true, overtimeCalculationMethod: OvertimeCalculationMethod.RoundByHalfHour,
            overtimeCapType: OvertimeCapType.Period, overtimeCapMinutes: 60);

    private DailyAttendance StoredDay(Employee employee, DateTime date, int raw, int effective)
    {
        var day = DailyAttendance.Create(employee.Id, date, _shift, date.AddHours(8), date.AddHours(16).AddMinutes(raw), false, overtimeAuthorized: true);
        day.SetCalculatedOvertime(raw, effective);
        return day;
    }

    private static (Mock<IDailyAttendanceRepository>, Mock<IEmployeeRepository>) Repos(Employee employee, params DailyAttendance[] days)
    {
        var daily = new Mock<IDailyAttendanceRepository>();
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(days.ToList());
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { employee });
        return (daily, employees);
    }

    [Fact]
    public async Task AdvancedReport_RangeSpanningTwoPeriods_DoesNotCapTheTotalAgain()
    {
        var employee = CreateEmployee();
        // Dos semanas (dos períodos): el motor guardó 60 min en cada una, ya con el tope de su período.
        var week1 = StoredDay(employee, new DateTime(2026, 8, 3), 120, 60);
        var week2 = StoredDay(employee, new DateTime(2026, 8, 10), 120, 60);
        var (daily, employees) = Repos(employee, week1, week2);
        var departments = new Mock<IDepartmentRepository>();
        departments.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());
        var handler = new GetAdvancedAttendanceReportQueryHandler(daily.Object, employees.Object, departments.Object, positions.Object, branches.Object,
            new Mock<ISystemConfigurationRepository>().Object);

        var result = await handler.Handle(new GetAdvancedAttendanceReportQuery(new DateTime(2026, 8, 3), new DateTime(2026, 8, 16), "HorasExtra"), CancellationToken.None);

        result.Single().TotalMetric.Should().Be(120, "cada período ya está topado; el total del rango es la suma");
    }

    [Fact]
    public async Task AttendanceReport_ShowsStoredOvertime_WithoutRoundingAgainWithTheCurrentMethod()
    {
        var employee = CreateEmployee();
        // El motor guardó 45 min (calculados cuando el empleado no redondeaba); hoy el empleado redondea por media hora.
        var day = StoredDay(employee, new DateTime(2026, 8, 3), 45, 45);
        var (daily, employees) = Repos(employee, day);
        var departments = new Mock<IDepartmentRepository>();
        departments.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());
        var handler = new GetAttendanceReportQueryHandler(daily.Object, employees.Object, branches.Object, departments.Object, positions.Object,
            new Mock<ISystemConfigurationRepository>().Object);

        var row = (await handler.Handle(new GetAttendanceReportQuery(new DateTime(2026, 8, 3), new DateTime(2026, 8, 3)), CancellationToken.None)).Single();

        row.OvertimeMinutes.Should().Be(45);
        row.RoundedOvertimeMinutes.Should().Be(45, "la exportación y el correo diario usan el valor guardado, no lo redondean de nuevo");
    }
}
