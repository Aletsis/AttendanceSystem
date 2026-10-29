using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

public class SendEmployeeToDeviceCommandHandlerTests
{
    private readonly Mock<IDeviceClientFactory> _clients = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IExternalEmployeeRepository> _externalEmployees = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Device _device = Device.Create("1", "Reloj", "192.168.1.200", 4370, DeviceBrand.ZKTeco);
    private readonly Branch _b01 = Branch.Create("B01", "Norte", null, isExternal: true);
    private readonly Branch _c01 = Branch.Create("C01", "Sur", null, isExternal: true);
    private readonly SendEmployeeToDeviceCommandHandler _handler;

    public SendEmployeeToDeviceCommandHandlerTests()
    {
        _devices.Setup(r => r.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _clients.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(_client.Object);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client.Setup(c => c.SetUserAsync(It.IsAny<DeviceUserDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _branches.Setup(r => r.GetByIdAsync(_b01.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_b01);
        _branches.Setup(r => r.GetByIdAsync(_c01.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_c01);
        _handler = new SendEmployeeToDeviceCommandHandler(_clients.Object, _devices.Object, _employees.Object,
            _externalEmployees.Object, _branches.Object, Mock.Of<IUnitOfWork>(), NullLogger<SendEmployeeToDeviceCommandHandler>.Instance);
    }

    private void Externals(params ExternalEmployee[] employees) =>
        _externalEmployees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(employees);

    [Fact]
    public async Task Handle_ByNumberRepeatedInTwoBranches_FailsWithoutTouchingTheDevice()
    {
        Externals(ExternalEmployee.Create(_b01.Id, "123", "Ana", "López"), ExternalEmployee.Create(_c01.Id, "123", "Luis", "Pérez"));

        var result = await _handler.Handle(new SendEmployeeToDeviceCommand("123", "1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("2 sucursales externas");
        _client.Verify(c => c.SetUserAsync(It.IsAny<DeviceUserDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ByUniqueNumber_SendsTheBranchPrefixedId()
    {
        Externals(ExternalEmployee.Create(_b01.Id, "123", "Ana", "López"), ExternalEmployee.Create(_c01.Id, "456", "Luis", "Pérez"));

        var result = await _handler.Handle(new SendEmployeeToDeviceCommand("123", "1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _client.Verify(c => c.SetUserAsync(It.Is<DeviceUserDto>(u => u.UserId == "B01123"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
