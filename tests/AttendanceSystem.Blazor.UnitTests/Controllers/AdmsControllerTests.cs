using System.Text;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
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

    private readonly AdmsController _controller;
    private readonly Employee _employee;
    private readonly Device _device;

    public AdmsControllerTests()
    {
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
            _jobSchedulerMock.Object
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
}
