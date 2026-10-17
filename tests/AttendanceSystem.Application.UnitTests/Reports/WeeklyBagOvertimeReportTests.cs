using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Reports;

public class WeeklyBagOvertimeReportTests
{
    private readonly BranchId _branchId = BranchId.CreateNew();
    private readonly DepartmentId _departmentId = DepartmentId.CreateNew();
    private readonly PositionId _positionId = PositionId.CreateNew();

    [Fact]
    public void CalculateEffectiveOvertime_WeeklyShift_ShouldAccumulateHoursAndTriggerOvertimeOnlyAfterGoal()
    {
        // Arrange
        // Semana laboral inicia en Lunes. Turno de 40h semanales.
        var weeklyShift = Shift.Create(
            name: "Bolsa Semanal 40h",
            startTime: TimeSpan.Zero,
            toleranceMinutes: 0,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: TimeSpan.FromHours(40),
            punchTrackingMode: PunchTrackingMode.SingleInterval,
            hasEntryWindow: false);

        var employee = Employee.Create(
            id: EmployeeId.From("EMP-WEEKLY-1"),
            firstName: "Pedro",
            lastName: "Ramirez",
            email: "pedro@empresa.com",
            phoneNumber: "555-9999",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Flexible,
            scheduleId: weeklyShift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true,
            overtimeCalculationMethod: OvertimeCalculationMethod.NoRounding);

        // Lunes a Viernes de la semana del 10 al 16 de Agosto 2026 (10 de Agosto es Lunes)
        // Lunes: 9h (540m) -> Acumulado = 9h (540m). Overtime = 0
        // Martes: 9h (540m) -> Acumulado = 18h (1080m). Overtime = 0
        // Miércoles: 9h (540m) -> Acumulado = 27h (1620m). Overtime = 0
        // Jueves: 9h (540m) -> Acumulado = 36h (2160m). Overtime = 0
        // Viernes: 9h (540m) -> Acumulado = 45h (2700m). Supera 40h (2400m). Overtime = 5h (300m)
        // Sábado: 4h (240m) -> Acumulado = 49h (2940m). Meta ya cubierta. Overtime = 4h (240m)
        var monday = new DateTime(2026, 8, 10);
        var records = new List<DailyAttendance>
        {
            DailyAttendance.Create(employee.Id, monday, weeklyShift, monday.AddHours(8), monday.AddHours(17), false, overtimeAuthorized: true, totalWorkedMinutes: 540),
            DailyAttendance.Create(employee.Id, monday.AddDays(1), weeklyShift, monday.AddDays(1).AddHours(8), monday.AddDays(1).AddHours(17), false, overtimeAuthorized: true, totalWorkedMinutes: 540),
            DailyAttendance.Create(employee.Id, monday.AddDays(2), weeklyShift, monday.AddDays(2).AddHours(8), monday.AddDays(2).AddHours(17), false, overtimeAuthorized: true, totalWorkedMinutes: 540),
            DailyAttendance.Create(employee.Id, monday.AddDays(3), weeklyShift, monday.AddDays(3).AddHours(8), monday.AddDays(3).AddHours(17), false, overtimeAuthorized: true, totalWorkedMinutes: 540),
            DailyAttendance.Create(employee.Id, monday.AddDays(4), weeklyShift, monday.AddDays(4).AddHours(8), monday.AddDays(4).AddHours(17), false, overtimeAuthorized: true, totalWorkedMinutes: 540),
            DailyAttendance.Create(employee.Id, monday.AddDays(5), weeklyShift, monday.AddDays(5).AddHours(8), monday.AddDays(5).AddHours(12), false, overtimeAuthorized: true, totalWorkedMinutes: 240)
        };

        // Act
        var result = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(
            records,
            employee,
            DayOfWeek.Monday);

        // Assert
        result[records[0].Id].Should().Be(0, "Lunes no supera la meta semanal de 40h");
        result[records[1].Id].Should().Be(0, "Martes no supera la meta semanal de 40h");
        result[records[2].Id].Should().Be(0, "Miércoles no supera la meta semanal de 40h");
        result[records[3].Id].Should().Be(0, "Jueves no supera la meta semanal de 40h");
        result[records[4].Id].Should().Be(300, "Viernes cruzó la meta semanal: 45h - 40h = 5h (300 min) extras");
        result[records[5].Id].Should().Be(240, "Sábado es todo tiempo extra al estar ya cubierta la meta de 40h");
    }

    [Fact]
    public void CalculateEffectiveOvertime_WeeklyShift_WithEmployeeHalfHourRounding_ShouldRoundOvertime()
    {
        // Arrange
        var weeklyShift = Shift.Create(
            name: "Bolsa Semanal 40h",
            startTime: TimeSpan.Zero,
            toleranceMinutes: 0,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: TimeSpan.FromHours(40),
            punchTrackingMode: PunchTrackingMode.SingleInterval,
            hasEntryWindow: false);

        var employee = Employee.Create(
            id: EmployeeId.From("EMP-WEEKLY-2"),
            firstName: "Lucia",
            lastName: "Mendez",
            email: "lucia@empresa.com",
            phoneNumber: "555-8888",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Female,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Flexible,
            scheduleId: weeklyShift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true,
            overtimeCalculationMethod: OvertimeCalculationMethod.RoundByHalfHour);

        var monday = new DateTime(2026, 8, 10);
        // Lunes a Jueves: 10h diarias (600m) = 40h exactas
        // Viernes: 1h y 25 minutos laborados (85 minutos). Con RoundByHalfHour -> redondea a 60 minutos (1h)
        var records = new List<DailyAttendance>
        {
            DailyAttendance.Create(employee.Id, monday, weeklyShift, monday.AddHours(8), monday.AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600),
            DailyAttendance.Create(employee.Id, monday.AddDays(1), weeklyShift, monday.AddDays(1).AddHours(8), monday.AddDays(1).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600),
            DailyAttendance.Create(employee.Id, monday.AddDays(2), weeklyShift, monday.AddDays(2).AddHours(8), monday.AddDays(2).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600),
            DailyAttendance.Create(employee.Id, monday.AddDays(3), weeklyShift, monday.AddDays(3).AddHours(8), monday.AddDays(3).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600),
            DailyAttendance.Create(employee.Id, monday.AddDays(4), weeklyShift, monday.AddDays(4).AddHours(8), monday.AddDays(4).AddHours(9).AddMinutes(25), false, overtimeAuthorized: true, totalWorkedMinutes: 85)
        };

        // Act
        var result = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(
            records,
            employee,
            DayOfWeek.Monday);

        // Assert
        result[records[3].Id].Should().Be(0, "Jueves llega a 40h exactas, 0 extras");
        result[records[4].Id].Should().Be(60, "Viernes hace 85 min extras redondeados a media hora hacia abajo = 60 min");
    }

    [Fact]
    public async Task GetAttendanceReportQuery_WeeklyBagShift_ShouldMarkOvertimeCorrectlyInReport()
    {
        // Arrange
        var weeklyShift = Shift.Create(
            name: "Bolsa Semanal 40h",
            startTime: TimeSpan.Zero,
            toleranceMinutes: 0,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: TimeSpan.FromHours(40),
            punchTrackingMode: PunchTrackingMode.SingleInterval,
            hasEntryWindow: false);

        var employee = Employee.Create(
            id: EmployeeId.From("EMP-WEEKLY-3"),
            firstName: "Sofia",
            lastName: "Castro",
            email: "sofia@empresa.com",
            phoneNumber: "555-7777",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Female,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Flexible,
            scheduleId: weeklyShift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true,
            overtimeCalculationMethod: OvertimeCalculationMethod.NoRounding);

        var monday = new DateTime(2026, 8, 10);
        // 4 días de 10h (Lunes-Jueves) y Viernes 5h -> Viernes hace 5h extras
        var record1 = DailyAttendance.Create(employee.Id, monday, weeklyShift, monday.AddHours(8), monday.AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600);
        var record2 = DailyAttendance.Create(employee.Id, monday.AddDays(1), weeklyShift, monday.AddDays(1).AddHours(8), monday.AddDays(1).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600);
        var record3 = DailyAttendance.Create(employee.Id, monday.AddDays(2), weeklyShift, monday.AddDays(2).AddHours(8), monday.AddDays(2).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600);
        var record4 = DailyAttendance.Create(employee.Id, monday.AddDays(3), weeklyShift, monday.AddDays(3).AddHours(8), monday.AddDays(3).AddHours(18), false, overtimeAuthorized: true, totalWorkedMinutes: 600);
        var record5 = DailyAttendance.Create(employee.Id, monday.AddDays(4), weeklyShift, monday.AddDays(4).AddHours(8), monday.AddDays(4).AddHours(13), false, overtimeAuthorized: true, totalWorkedMinutes: 300);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();
        var configRepoMock = new Mock<ISystemConfigurationRepository>();

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { record1, record2, record3, record4, record5 });

        employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { employee });
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());
        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());

        var handler = new GetAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            branchRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object,
            configRepoMock.Object);

        // Act: Consulta el reporte de toda la semana
        var query = new GetAttendanceReportQuery(monday, monday.AddDays(6));
        var report = (await handler.Handle(query, CancellationToken.None)).ToList();

        // Assert
        report.Should().HaveCount(5);
        report[0].OvertimeMinutes.Should().Be(0);
        report[1].OvertimeMinutes.Should().Be(0);
        report[2].OvertimeMinutes.Should().Be(0);
        report[3].OvertimeMinutes.Should().Be(0);
        report[4].OvertimeMinutes.Should().Be(300, "Viernes debe reflejar las 5 horas extras semanales");
    }
}
