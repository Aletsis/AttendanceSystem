using System.Text;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Blazor.Server.Controllers;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Controllers;

public class AdmsControllerTests
{
    private readonly Mock<ILogger<AdmsController>> _loggerMock = new();
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IDeviceRepository> _deviceRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAdmsCommandService> _admsCommandServiceMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepositoryMock = new();
    private readonly Mock<IDownloadLogRepository> _downloadLogRepositoryMock = new();
    private readonly Mock<IBranchRepository> _branchRepositoryMock = new();
    private readonly Mock<ILogTransferService> _logTransferServiceMock = new();
    private readonly Mock<IAttendanceJobScheduler> _jobSchedulerMock = new();
    private readonly Mock<IExternalAttendanceLogRepository> _externalLogRepositoryMock = new();
    private readonly Mock<ITimeNormalizer> _timeNormalizerMock = new();

    private readonly AdmsController _controller;
    private readonly Employee _employee;
    private readonly Device _device;

    public AdmsControllerTests()
    {
        _timeNormalizerMock.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        _timeNormalizerMock.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateOnly.FromDateTime(dt));
        _timeNormalizerMock.Setup(t => t.ToCivilTime(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified));
        _timeNormalizerMock.Setup(t => t.ResolveTimeZone(It.IsAny<string>())).Returns(TimeZoneInfo.Utc);

        _controller = new AdmsController(
            _loggerMock.Object,
            _mediatorMock.Object,
            _deviceRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _admsCommandServiceMock.Object,
            _employeeRepositoryMock.Object,
            _downloadLogRepositoryMock.Object,
            _branchRepositoryMock.Object,
            _logTransferServiceMock.Object,
            _jobSchedulerMock.Object,
            _externalLogRepositoryMock.Object,
            _timeNormalizerMock.Object
        );

        _employee = Employee.Create(
            EmployeeId.From("475"),
            "Isela",
            "Rodriguez",
            "isela@example.com",
            "1234567890",
            DateTime.UtcNow,
            Gender.Female,
            BranchId.From(Guid.NewGuid()),
            DepartmentId.From(Guid.NewGuid()),
            PositionId.From(Guid.NewGuid()),
            shiftType: ShiftType.Matutino
        );

        _device = Device.Create(
            "1",
            "Reloj Principal",
            "192.168.0.205",
            8083,
            DeviceBrand.ZKTeco,
            downloadMethod: DeviceDownloadMethod.Adms,
            serialNumber: "CL5Z202660239"
        );
        _device.SetDeviceType("acc");

        _deviceRepositoryMock.Setup(r => r.GetBySerialNumberAsync("CL5Z202660239", default))
            .ReturnsAsync(_device);
        _employeeRepositoryMock.Setup(r => r.GetByIdAsync(EmployeeId.From("475"), default))
            .ReturnsAsync(_employee);
    }

    private void SetRequestBody(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(bytes);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Theory]
    [InlineData("ID=10&Return=0&CMD=INFO")]
    [InlineData("ID=10&Return=-1&CMD=INFO\nFirmwareVersion=failed")]
    [InlineData("ID=10&Return=0&CMD=INFO\nUserCount=invalid")]
    [InlineData("ID=10&Return=0&CMD=INFO\n~SerialNumber=OTHER\nFWVersion=1.0")]
    public async Task DeviceCmd_InfoWithoutValidTechnicalData_DoesNotCompleteInfoRequest(string body)
    {
        SetRequestBody(body);

        await _controller.DeviceCmd("CL5Z202660239");

        _admsCommandServiceMock.Verify(s => s.NotifyDeviceInfoReceived(
            It.IsAny<string>(), It.IsAny<AttendanceSystem.Application.DTOs.DeviceInfoDto>(), It.IsAny<string?>()), Times.Never);
        _deviceRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Device>(), default), Times.Never);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("&")]
    public async Task DeviceCmd_InfoUpdatesOnlyTechnicalData(string separator)
    {
        SetRequestBody("ID=10&Return=0&CMD=INFO\n" +
            string.Join(separator, "FWVersion=1.2.3", "UserCount=42", "FPCount=3", "TransactionCount=123"));

        await _controller.DeviceCmd("CL5Z202660239");

        _admsCommandServiceMock.Verify(s => s.NotifyDeviceInfoReceived("CL5Z202660239",
            It.Is<AttendanceSystem.Application.DTOs.DeviceInfoDto>(i =>
                i.FirmwareVersion == "1.2.3" && i.UserCount == 42 &&
                i.FingerprintCount == 3 && i.AttendanceRecordCount == 123), "10"), Times.Once);
        _admsCommandServiceMock.Verify(s => s.AcknowledgeCommandExecutionAsync(
            "CL5Z202660239", "10", 0, null, default), Times.Once);
        _mediatorMock.VerifyNoOtherCalls();
        _employeeRepositoryMock.VerifyNoOtherCalls();
        _admsCommandServiceMock.Verify(s => s.EnqueueCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task DeviceCmd_InfoScalesCapacitiesAndSuppressesFingerprintsOnNfModel()
    {
        // MiniAC Plus: ZAM170-NF-Ver1.1.20, ~MaxUserCount=30, ~MaxAttLogCount=15, ~MaxFaceCount=3000, FPCount=12, ~MaxFingerCount=100
        SetRequestBody("ID=20&Return=0&CMD=INFO\n" +
            "FWVersion=ZAM170-NF-Ver1.1.20\n" +
            "UserCount=354\n" +
            "~MaxUserCount=30\n" +
            "FPCount=12\n" +
            "~MaxFingerCount=100\n" +
            "FaceCount=280\n" +
            "~MaxFaceCount=3000\n" +
            "AttLogCount=0\n" +
            "TransactionCount=1500\n" +
            "~MaxAttLogCount=15\n");

        await _controller.DeviceCmd("CL5Z202660239");

        _admsCommandServiceMock.Verify(s => s.NotifyDeviceInfoReceived("CL5Z202660239",
            It.Is<AttendanceSystem.Application.DTOs.DeviceInfoDto>(i =>
                i.FirmwareVersion == "ZAM170-NF-Ver1.1.20" &&
                i.UserCount == 354 &&
                i.UserCapacity == 3000 &&
                i.FingerprintCount == 0 &&
                i.FingerprintCapacity == 0 &&
                i.FaceCount == 280 &&
                i.FaceCapacity == 3000 &&
                i.AttendanceRecordCount == 1500 &&
                i.AttendanceRecordCapacity == 150000), "20"), Times.Once);
    }

    [Fact]
    public async Task QueryData_WhenTransactionTableQueried_ShouldReturnOkContentAndNotIncludeCount()
    {
        // Arrange: Línea de Transaction del log real
        var line = "transaction cardno=0\tpin=475\tverified=15\tdoorid=1\teventtype=3\tinoutstate=0\ttime_second=857920849\tindex=521745\tmaskflag=1\ttemperature=36.2";
        SetRequestBody(line);

        _mediatorMock.Setup(m => m.Send(It.IsAny<IRequest<Result<int>>>(), default))
            .ReturnsAsync(Result<int>.Success(1));

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "transaction", type: "tabledata");

        // Assert: Debe ser estrictamente "OK" (no "OK: 1") para permitir la paginación ZKTeco (packcnt=36)
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK");
    }

    [Fact]
    public async Task QueryData_WhenTemplatev10Received_ShouldSaveFingerprintWithIndexAndTemplate()
    {
        // Arrange: Línea de templatev10 del log real
        var line = "templatev10 size=1224\tuid=2\tpin=475\tfingerid=6\tvalid=1\ttemplate=StVTUzIxAAADlpY";
        SetRequestBody(line);

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "templatev10", type: "tabledata");

        // Assert
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK");

        _employee.Fingerprints.Should().ContainSingle(f => f.FingerIndex == 6 && f.Template == "StVTUzIxAAADlpY");
        _employeeRepositoryMock.Verify(r => r.Update(_employee), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task QueryData_WhenBiodataReceivedWithTmpField_ShouldSaveFaceTemplate()
    {
        // Arrange: Línea de BIODATA del log real con Type=9 y tmp=
        var line = "biodata pin=475\tno=0\tindex=0\tvalid=1\tduress=0\ttype=9\tmajorver=58\tminorver=10\tformat=0\ttmp=apUBEBAFACE123";
        SetRequestBody(line);

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "biodata", type: "tabledata");

        // Assert
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK");

        _employee.FaceTemplate.Should().Be("apUBEBAFACE123");
        _employeeRepositoryMock.Verify(r => r.Update(_employee), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task QueryData_WhenBiophotoReceived_ShouldUpdateEmployeePhoto()
    {
        // Arrange: Línea de biophoto del log real
        var line = "biophoto pin=475\tfilename=475.jpg\ttype=9\tsize=45748\tcontent=BASE64PHOTODATA";
        SetRequestBody(line);

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "biophoto", type: "tabledata");

        // Assert
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK");

        _employee.Photo.Should().Be("BASE64PHOTODATA");
        _employeeRepositoryMock.Verify(r => r.Update(_employee), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task QueryData_WhenUserReceivedWithCardnoAndPrivilege_ShouldUpdateCardAndPrivilege()
    {
        // Arrange: Línea de user del log real con cardno y privilege=14 (SuperAdmin)
        var line = "user uid=2\tcardno=987654321\tpin=475\tpassword=1593\tgroup=1\tstarttime=0\tendtime=0\tname=RODRIGUEZ MIRELES ISELA ELIZABETH\tprivilege=14\tdisable=0\tverify=0";
        SetRequestBody(line);

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "user", type: "tabledata");

        // Assert
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK");

        _employee.CardNumber.Should().Be("987654321");
        _employee.DevicePassword.Should().Be("1593");
        _employee.DevicePrivilege.Should().Be(DevicePrivilege.SuperAdmin);
        _employeeRepositoryMock.Verify(r => r.Update(_employee), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ReceiveData_WhenDeviceNotFound_ShouldReturnUnauthorized()
    {
        // Arrange
        SetRequestBody("dummy");
        _deviceRepositoryMock.Setup(r => r.GetBySerialNumberAsync("UNKNOWN_DEVICE", default))
            .ReturnsAsync((Device?)null);

        // Act
        var result = await _controller.ReceiveData("UNKNOWN_DEVICE", "ATTLOG");

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ReceiveData_WhenDeviceInactive_ShouldReturnUnauthorized()
    {
        // Arrange
        var inactiveDevice = Device.Create(
            "2",
            "Inactive Clock",
            "192.168.0.206",
            8083,
            DeviceBrand.ZKTeco,
            downloadMethod: DeviceDownloadMethod.Adms,
            serialNumber: "INACTIVE_SN"
        );
        inactiveDevice.Deactivate();

        _deviceRepositoryMock.Setup(r => r.GetBySerialNumberAsync("INACTIVE_SN", default))
            .ReturnsAsync(inactiveDevice);
        SetRequestBody("dummy");

        // Act
        var result = await _controller.ReceiveData("INACTIVE_SN", "ATTLOG");

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ReceiveData_WhenProcessAttLogsFails_ShouldReturn500InternalServerError()
    {
        // Arrange
        var line = "1\t2026-03-01 08:00:00\t1\t1";
        SetRequestBody(line);

        _mediatorMock.Setup(m => m.Send(It.IsAny<IRequest<Result<int>>>(), default))
            .ReturnsAsync(Result<int>.Failure("Database connection dropped"));

        // Act
        var result = await _controller.ReceiveData("CL5Z202660239", "ATTLOG");

        // Assert: must return HTTP 500 to prevent device cursor advance
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        statusResult.Value.Should().Be("ERROR: Batch persistence failed\n");
    }

    [Fact]
    public async Task QueryData_WhenProcessAttLogsFails_ShouldReturn500InternalServerError()
    {
        // Arrange
        var line = "1\t2026-03-01 08:00:00\t1\t1";
        SetRequestBody(line);

        _mediatorMock.Setup(m => m.Send(It.IsAny<IRequest<Result<int>>>(), default))
            .ReturnsAsync(Result<int>.Failure("DB timeout"));

        // Act
        var result = await _controller.QueryData("CL5Z202660239", tablename: "Transaction", type: "tabledata");

        // Assert: must return HTTP 500 so device does not advance cursor or drop records
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        statusResult.Value.Should().Be("ERROR: Batch persistence failed\n");
    }

    [Fact]
    public void DebugPendingCommands_ShouldHaveAuthorizeAttributeWithAdminRole()
    {
        // Arrange
        var method = typeof(AdmsController).GetMethod(nameof(AdmsController.GetPendingCommandsDebug));

        // Act
        var attr = method?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .FirstOrDefault() as Microsoft.AspNetCore.Authorization.AuthorizeAttribute;

        // Assert
        attr.Should().NotBeNull();
        attr!.Roles.Should().Be("Administrador");
    }

    [Fact]
    public async Task ReceiveData_WhenPartialLinesMalformed_ReturnsOkWithStrictCountOfProcessedRecords_H11()
    {
        // Arrange: 1 valid line and 1 malformed line
        var payload = "475\t2026-03-01 08:00:00\t1\t1\nmalformed_line_without_tabs";
        SetRequestBody(payload);

        _mediatorMock.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), default))
            .ReturnsAsync(Result<int>.Success(1));

        // Act
        var result = await _controller.ReceiveData("CL5Z202660239", "ATTLOG");

        // Assert: must return "OK: 1" (only confirmed records, not total lines)
        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.Content.Should().Be("OK: 1");

        // Cursor must NOT advance because batch had malformed line
        _deviceRepositoryMock.Verify(r => r.UpdateLastAttLogTimestampAsync(It.IsAny<string>(), It.IsAny<DateTime>(), default), Times.Never());
    }
}
