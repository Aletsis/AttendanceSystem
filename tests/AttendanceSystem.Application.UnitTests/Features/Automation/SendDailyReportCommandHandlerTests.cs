using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Automation;

public class SendDailyReportCommandHandlerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IReportExportService> _reportExportServiceMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<SendDailyReportCommandHandler>> _loggerMock;
    private readonly SendDailyReportCommandHandler _handler;

    public SendDailyReportCommandHandlerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _reportExportServiceMock = new Mock<IReportExportService>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<SendDailyReportCommandHandler>>();

        _handler = new SendDailyReportCommandHandler(
            _mediatorMock.Object,
            _reportExportServiceMock.Object,
            _emailServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenAutoReportIsDisabled_ShouldReturnSuccessWithoutSendingEmail()
    {
        // Arrange
        var configDto = new SystemConfigurationDto(
            CompanyName: "Mi Empresa",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            IsAutoReportEnabled: false,
            AutoReportEmails: "admin@empresa.com");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(configDto));

        // Act
        var result = await _handler.Handle(new SendDailyReportCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendReportAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<(string, byte[])>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAutoReportIsEnabled_ShouldGenerateAttachmentsAndSendEmail()
    {
        // Arrange
        var configDto = new SystemConfigurationDto(
            CompanyName: "Mi Empresa",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            IsAutoReportEnabled: true,
            AutoReportEmails: "reportes@empresa.com",
            AutoReportForToday: true);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(configDto));

        var reportRecords = new List<AttendanceReportViewDto>
        {
            new()
            {
                EmployeeId = "EMP-001",
                EmployeeName = "Carlos Gomez",
                BranchName = "Sucursal Norte",
                Date = DateTime.Today,
                IsAbsent = true,
                LateMinutes = 0,
                OvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding
            },
            new()
            {
                EmployeeId = "EMP-002",
                EmployeeName = "Laura Ramos",
                BranchName = "Sucursal Sur",
                Date = DateTime.Today,
                IsAbsent = false,
                LateMinutes = 20,
                OvertimeMinutes = 60,
                OvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetAttendanceReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reportRecords);

        _reportExportServiceMock
            .Setup(s => s.GeneratePdf(reportRecords, It.IsAny<DateTime>(), It.IsAny<DateTime>(), "Mi Empresa", null))
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        _reportExportServiceMock
            .Setup(s => s.GenerateExcel(reportRecords, It.IsAny<DateTime>(), It.IsAny<DateTime>(), "Mi Empresa", null, true))
            .Returns(new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // PK.. (Zip/Xlsx)

        // Act
        var result = await _handler.Handle(new SendDailyReportCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendReportAsync(
            It.Is<string>(s => s.Contains("Reporte Diario de Asistencia")),
            It.Is<string>(b => b.Contains("Total de Faltas:</b> 1") && b.Contains("Laura Ramos")),
            "reportes@empresa.com",
            It.Is<List<(string Name, byte[] Content)>>(att => att.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
