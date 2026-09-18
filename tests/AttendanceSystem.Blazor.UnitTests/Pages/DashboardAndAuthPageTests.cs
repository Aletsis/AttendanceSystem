using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;
using Microsoft.AspNetCore.Components;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class DashboardAndAuthPageTests : BlazorTestBase
{
    [Fact]
    public void IndexPage_ShouldRenderDashboard_WithMetrics()
    {
        // Setup dashboard data
        var employees = new List<EmployeeDto>
        {
            new() { Id = "1", FirstName = "Carlos", LastName = "Ruiz", FullName = "Carlos Ruiz" }
        };

        var devices = new List<DeviceDto>
        {
            new() { DeviceId = "DEV01", Name = "Checador 1", IpAddress = "192.168.1.201", Port = 4370 }
        };

        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Matriz" } };
        var departments = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Sistemas" } };
        var positions = new List<PositionDto> { new() { Id = Guid.NewGuid(), Name = "Desarrollador" } };
        var shifts = new List<ShiftDto> { new() { Id = Guid.NewGuid(), Name = "Matutino" } };
        var config = new SystemConfigurationDto { BackupDirectory = "/backups" };
        var report = new List<AttendanceReportViewDto>();

        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAllDevicesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DeviceDto>>.Success(devices));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(departments));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<PositionDto>>.Success(positions));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetShiftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<ShiftDto>>.Success(shifts));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(config));
        MediatorMock.Setup(m => m.Send(It.IsAny<GetAttendanceReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var cut = RenderComponent<AttendanceSystem.Blazor.Server.Components.Pages.Index>();

        cut.Markup.Should().Contain("Panel de Control");
        cut.Markup.Should().Contain("Bienvenido al sistema de control de asistencia");
    }

    [Fact]
    public void LoginPage_ShouldRenderCorrectly()
    {
        var cut = RenderComponent<Login>();

        cut.Markup.Should().Contain("BIENVENIDO");
        cut.Markup.Should().Contain("Usuario");
        cut.Markup.Should().Contain("Contraseña");
        cut.Markup.Should().Contain("INGRESAR");
    }

    [Fact]
    public void LogoutPage_ShouldRenderRedirectingState()
    {
        var cut = RenderComponent<Logout>();
        cut.Markup.Should().NotBeNull();
    }

    [Fact]
    public void ErrorPage_ShouldRenderErrorMessage()
    {
        var cut = RenderComponent<Error>();
        cut.Markup.Should().Contain("Error");
    }
}
