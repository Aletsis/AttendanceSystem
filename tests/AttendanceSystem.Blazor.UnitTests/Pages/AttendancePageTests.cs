using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceLogs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAbsenteeismAnalysis;
using AttendanceSystem.Application.Features.Attendance.Queries.GetTardinessAnalysis;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class AttendancePageTests : BlazorTestBase
{
    [Fact]
    public void AttendanceLogsPage_ShouldRenderWithoutException()
    {
        var employees = new List<EmployeeDto>
        {
            new() { Id = "EMP01", FirstName = "Mario", LastName = "Bros", FullName = "Mario Bros" }
        };

        MediatorMock
            .Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        MediatorMock
            .Setup(m => m.Send(It.IsAny<GetAttendanceLogsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceLogViewDto>());

        var cut = RenderComponent<AttendanceLogs>();

        cut.Markup.Should().Contain("Bitácora de Asistencia");
        cut.Markup.Should().Contain("Empleado");
    }

    [Fact]
    public void AttendanceReportPage_ShouldRenderFiltersAndActions()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Centro" } };
        var employees = new List<EmployeeDto> { new() { Id = "1", FirstName = "Ana", LastName = "Sol", FullName = "Ana Sol" } };
        var config = new SystemConfigurationDto { LateToleranceMinutes = TimeSpan.FromMinutes(15) };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(config));

        var cut = RenderComponent<AttendanceReport>();

        cut.Markup.Should().Contain("Reporte de Asistencia");
        cut.Markup.Should().Contain("Desde");
        cut.Markup.Should().Contain("Hasta");
    }

    [Fact]
    public void CalculateAttendancePage_ShouldRenderCalculationScopes()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Norte" } };
        var employees = new List<EmployeeDto> { new() { Id = "10", FirstName = "Luis", LastName = "Paz", FullName = "Luis Paz" } };
        var config = new SystemConfigurationDto();

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(config));

        var cut = RenderComponent<CalculateAttendance>();

        cut.Markup.Should().Contain("Cálculo de Asistencia");
        cut.Markup.Should().Contain("Por Día");
        cut.Markup.Should().Contain("Rango de Fechas");
    }

    [Fact]
    public void AttendanceCardsPage_ShouldRenderWithoutException()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Oriente" } };
        var depts = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Ventas" } };
        var employees = new List<EmployeeDto> { new() { Id = "20", FirstName = "Elena", LastName = "Vega", FullName = "Elena Vega" } };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(depts));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        var cut = RenderComponent<AttendanceCards>();

        cut.Markup.Should().Contain("Impresión de Checadores");
    }

    [Fact]
    public void AbsenteeismAnalysisPage_ShouldRenderWithoutException()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Matriz" } };
        var depts = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Logística" } };
        var employees = new List<EmployeeDto> { new() { Id = "30", FirstName = "Sara", LastName = "Cruz", FullName = "Sara Cruz" } };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(depts));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        var cut = RenderComponent<AbsenteeismAnalysis>();

        cut.Markup.Should().Contain("Análisis de Ausentismo");
    }

    [Fact]
    public void TardinessAnalysisPage_ShouldRenderWithoutException()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Matriz" } };
        var depts = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Calidad" } };
        var employees = new List<EmployeeDto> { new() { Id = "40", FirstName = "Hugo", LastName = "Rios", FullName = "Hugo Rios" } };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(depts));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        var cut = RenderComponent<TardinessAnalysis>();

        cut.Markup.Should().Contain("Análisis de Retardos");
    }

    [Fact]
    public void AdvancedAttendanceReportsPage_ShouldRenderWithoutException()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Matriz" } };
        var depts = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Compras" } };
        var employees = new List<EmployeeDto> { new() { Id = "50", FirstName = "Paco", LastName = "Leon", FullName = "Paco Leon" } };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(depts));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        var cut = RenderComponent<AdvancedAttendanceReports>();

        cut.Markup.Should().Contain("Reportes Avanzados");
    }
}
