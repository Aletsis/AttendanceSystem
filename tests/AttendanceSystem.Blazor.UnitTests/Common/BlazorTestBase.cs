using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using MudBlazor.Services;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Employees;

namespace AttendanceSystem.Blazor.UnitTests.Common;

public abstract class BlazorTestBase : TestContext
{
    protected Mock<IMediator> MediatorMock { get; }
    protected Mock<ISender> SenderMock { get; }
    protected Mock<IImportService> ImportServiceMock { get; }
    protected Mock<IReportExportService> ExportServiceMock { get; }
    protected Mock<IEmailService> EmailServiceMock { get; }

    protected BlazorTestBase()
    {
        // Add MudBlazor services and test JSInterop
        Services.AddMudServices();
        Services.AddHttpContextAccessor();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Setup test authorization
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("TestUser");
        authContext.SetRoles("Administrador", "Supervisor", "Usuario");

        MediatorMock = new Mock<IMediator>();
        SenderMock = new Mock<ISender>();
        ImportServiceMock = new Mock<IImportService>();
        ExportServiceMock = new Mock<IReportExportService>();
        EmailServiceMock = new Mock<IEmailService>();

        var httpFactoryMock = new Mock<System.Net.Http.IHttpClientFactory>();
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<AttendanceSystem.Blazor.Server.Services.UpdateCheckerService>>();
        var updateChecker = new AttendanceSystem.Blazor.Server.Services.UpdateCheckerService(httpFactoryMock.Object, loggerMock.Object);

        Services.AddSingleton(MediatorMock.Object);
        Services.AddSingleton(SenderMock.Object);
        Services.AddSingleton(ImportServiceMock.Object);
        Services.AddSingleton(ExportServiceMock.Object);
        Services.AddSingleton(EmailServiceMock.Object);
        Services.AddSingleton(updateChecker);

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        var defaultConfig = new AttendanceSystem.Application.DTOs.SystemConfigurationDto();
        var emptyBranches = new List<AttendanceSystem.Application.DTOs.BranchDto>();
        var emptyDepts = new List<AttendanceSystem.Application.DTOs.DepartmentDto>();
        var emptyPositions = new List<AttendanceSystem.Application.DTOs.PositionDto>();
        var emptyShifts = new List<AttendanceSystem.Application.DTOs.ShiftDto>();
        var emptyEmployees = new List<EmployeeDto>();
        var emptyDevices = new List<AttendanceSystem.Application.DTOs.DeviceDto>();
        var emptyPaginatedEmployees = new AttendanceSystem.Application.Features.Employees.Queries.PaginatedEmployeesDto(new List<EmployeeDto>(), 0);

        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<AttendanceSystem.Application.DTOs.SystemConfigurationDto>.Success(defaultConfig)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.BranchDto>>.Success(emptyBranches)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.DepartmentDto>>.Success(emptyDepts)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.PositionDto>>.Success(emptyPositions)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.ShiftDto>>.Success(emptyShifts)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IReadOnlyList<EmployeeDto>>.Success(emptyEmployees)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<AttendanceSystem.Application.Features.Employees.Queries.PaginatedEmployeesDto>.Success(emptyPaginatedEmployees)));
        MediatorMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.DeviceDto>>.Success(emptyDevices)));
        MediatorMock.SetReturnsDefault(Task.FromResult<IEnumerable<AttendanceSystem.Application.DTOs.AttendanceReportViewDto>>(new List<AttendanceSystem.Application.DTOs.AttendanceReportViewDto>()));

        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<AttendanceSystem.Application.DTOs.SystemConfigurationDto>.Success(defaultConfig)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.BranchDto>>.Success(emptyBranches)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.DepartmentDto>>.Success(emptyDepts)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.PositionDto>>.Success(emptyPositions)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IEnumerable<AttendanceSystem.Application.DTOs.ShiftDto>>.Success(emptyShifts)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<IReadOnlyList<EmployeeDto>>.Success(emptyEmployees)));
        SenderMock.SetReturnsDefault(Task.FromResult(AttendanceSystem.Application.Common.Result<AttendanceSystem.Application.Features.Employees.Queries.PaginatedEmployeesDto>.Success(emptyPaginatedEmployees)));
    }
}
