using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Employees;

/// <summary>
/// Auditoría de tareas, bloque C: una conexión por reloj para todos los empleados sin biometría, con el cliente según
/// el método del reloj y el reloj apartado; siempre se desconecta.
/// </summary>
public class SyncEmployeeBiometricsCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IDeviceClientFactory> _clients = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IDeviceLockService> _locks = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<string> _lockedDevices = new();

    public SyncEmployeeBiometricsCommandHandlerTests()
    {
        _clients.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(_client.Object);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _locks.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((id, action, _) => { _lockedDevices.Add(id); return action(); });
    }

    private SyncEmployeeBiometricsCommandHandler Handler() => new(
        _employees.Object, _devices.Object, _clients.Object, _locks.Object, _unitOfWork.Object,
        NullLogger<SyncEmployeeBiometricsCommandHandler>.Instance);

    private Device AddDevice(DeviceDownloadMethod method = DeviceDownloadMethod.Sdk)
    {
        var device = Device.Create("DEV-BIO", "Reloj", "10.0.0.5", 4370, downloadMethod: method, serialNumber: "SN-BIO");
        _devices.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        return device;
    }

    private Employee AddEmployee(string id, string? cardNumber = null)
    {
        var employee = Employee.Create(EmployeeId.From(id), "E", id, null, null, new DateTime(2024, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino, cardNumber: cardNumber);
        _employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        return employee;
    }

    private void OnDevice(string id, string card) =>
        _client.Setup(c => c.GetUserAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceUserDto(id, "E", "", 0, true, CardNumber: card));

    [Fact]
    public async Task AllEmployees_AreSyncedOverOneConnection_WithTheDeviceLocked()
    {
        var device = AddDevice();
        var first = AddEmployee("BIO-1");
        var second = AddEmployee("BIO-2");
        OnDevice("BIO-1", "111");
        OnDevice("BIO-2", "222");

        var result = await Handler().Handle(new SyncEmployeeBiometricsCommand(device.Id.Value, new[] { "BIO-1", "BIO-2" }), CancellationToken.None);

        result.Value.Should().Be(2);
        first.CardNumber.Should().Be("111");
        second.CardNumber.Should().Be("222");
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        _lockedDevices.Should().Equal(device.Id.Value);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnAdmsClock_UsesTheClientForItsMethod()
    {
        var device = AddDevice(DeviceDownloadMethod.Adms);
        AddEmployee("BIO-3");

        (await Handler().Handle(new SyncEmployeeBiometricsCommand(device.Id.Value, new[] { "BIO-3" }), CancellationToken.None))
            .IsSuccess.Should().BeTrue("el cliente ADMS devuelve null si no puede leer al usuario: no es un fallo");

        _clients.Verify(f => f.GetClient(device), Times.Once);
        _clients.Verify(f => f.GetClient(It.IsAny<DeviceBrand>()), Times.Never);
    }

    [Fact]
    public async Task EmployeesThatAlreadyHaveBiometrics_DoNotTouchTheDevice()
    {
        var device = AddDevice();
        AddEmployee("BIO-4", cardNumber: "999");

        var result = await Handler().Handle(new SyncEmployeeBiometricsCommand(device.Id.Value, new[] { "BIO-4", "NO-EXISTE" }), CancellationToken.None);

        result.Value.Should().Be(0);
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _lockedDevices.Should().BeEmpty();
    }

    [Fact]
    public async Task OneEmployeeFailing_DoesNotStopTheOthers_AndTheDeviceIsDisconnected()
    {
        var device = AddDevice();
        AddEmployee("BIO-5");
        var ok = AddEmployee("BIO-6");
        _client.Setup(c => c.GetUserAsync("BIO-5", It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("lectura cortada"));
        OnDevice("BIO-6", "666");

        var result = await Handler().Handle(new SyncEmployeeBiometricsCommand(device.Id.Value, new[] { "BIO-5", "BIO-6" }), CancellationToken.None);

        result.Value.Should().Be(1);
        ok.CardNumber.Should().Be("666");
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnExceptionTalkingToTheDevice_FailsAndStillDisconnects()
    {
        var device = AddDevice();
        AddEmployee("BIO-7");
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sin respuesta"));

        var result = await Handler().Handle(new SyncEmployeeBiometricsCommand(device.Id.Value, new[] { "BIO-7" }), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
