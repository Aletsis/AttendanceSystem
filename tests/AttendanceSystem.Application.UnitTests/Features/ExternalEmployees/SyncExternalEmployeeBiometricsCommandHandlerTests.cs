using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.ExternalEmployees.Commands.SyncExternalEmployeeBiometrics;
using AttendanceSystem.Application.Features.ExternalEmployees.Services;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.ExternalEmployees;

/// <summary>
/// Huellas de empleados externos copiadas del reloj local por su ID en el reloj (código de sucursal + número): una
/// conexión por reloj, con el reloj apartado, solo para los que no tienen huellas; siempre se desconecta.
/// </summary>
public class SyncExternalEmployeeBiometricsCommandHandlerTests
{
    private readonly Mock<IExternalEmployeeRepository> _externals = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IDeviceClientFactory> _clients = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IDeviceLockService> _locks = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Branch _b01 = Branch.Create("B01", "Norte", null, isExternal: true);
    private readonly Branch _local = Branch.Create("L01", "Matriz", null);
    private readonly Device _device = Device.Create("DEV-EXT", "Reloj", "10.0.0.5", 4370, serialNumber: "SN-EXT");
    private readonly List<string> _lockedDevices = new();

    public SyncExternalEmployeeBiometricsCommandHandlerTests()
    {
        _branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { _b01, _local });
        _devices.Setup(r => r.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _clients.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(_client.Object);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _locks.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((id, action, _) => { _lockedDevices.Add(id); return action(); });
    }

    private SyncExternalEmployeeBiometricsCommandHandler Handler() => new(
        _externals.Object, _branches.Object, _devices.Object, _clients.Object, _locks.Object, _unitOfWork.Object,
        NullLogger<SyncExternalEmployeeBiometricsCommandHandler>.Instance);

    private ExternalEmployee AddExternal(string number)
    {
        var employee = ExternalEmployee.Create(_b01.Id, number, "E", number);
        _externals.Setup(r => r.GetByBranchAndEmployeeNumberAsync(_b01.Id, number, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        return employee;
    }

    private void OnDevice(string deviceUserId, params (int Index, string Template)[] fingers) =>
        _client.Setup(c => c.GetUserAsync(deviceUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceUserDto(deviceUserId, "E", "", 0, true,
                Fingerprints: fingers.Select(f => new DeviceFingerprintDto(f.Index, f.Template)).ToList()));

    private Task<Result<int>> Run(params string[] ids) =>
        Handler().Handle(new SyncExternalEmployeeBiometricsCommand(_device.Id.Value, ids), CancellationToken.None);

    [Fact]
    public async Task Externals_AreSyncedOverOneConnection_WithTheDeviceLocked()
    {
        var first = AddExternal("123");
        var second = AddExternal("456");
        OnDevice("B01123", (6, "TPL-A"));
        OnDevice("B01456", (1, "TPL-B"), (2, "TPL-C"));

        var result = await Run("B01123", "B01456");

        result.Value.Should().Be(2);
        first.Fingerprints.Should().ContainSingle(f => f.FingerIndex == 6 && f.Template == "TPL-A");
        second.Fingerprints.Should().HaveCount(2);
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        _lockedDevices.Should().Equal(_device.Id.Value);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheClockIsAskedForTheIdItWasRegisteredWith_EvenIfTheCodeCameInAnotherCase()
    {
        var employee = AddExternal("123");
        OnDevice("B01123", (0, "TPL"));

        var result = await Run("b01123");

        result.Value.Should().Be(1);
        employee.Fingerprints.Should().ContainSingle();
    }

    [Fact]
    public async Task ExternalsWithFingerprints_UnknownOnes_AndLocalIds_DoNotTouchTheDevice()
    {
        var already = AddExternal("123");
        already.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(0, "TPL") });

        var result = await Run("B01123", "B01999", "L01123", "475");

        result.Value.Should().Be(0);
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _lockedDevices.Should().BeEmpty();
    }

    [Fact]
    public async Task OneExternalFailing_DoesNotStopTheOthers_AndTheDeviceIsDisconnected()
    {
        AddExternal("123");
        var ok = AddExternal("456");
        _client.Setup(c => c.GetUserAsync("B01123", It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("lectura cortada"));
        OnDevice("B01456", (3, "TPL"));

        var result = await Run("B01123", "B01456");

        result.Value.Should().Be(1);
        ok.Fingerprints.Should().ContainSingle();
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnExceptionTalkingToTheDevice_FailsAndStillDisconnects()
    {
        AddExternal("123");
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sin respuesta"));

        var result = await Run("B01123");

        result.IsSuccess.Should().BeFalse();
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ExternalEmployeeDeviceIdsTests
{
    private readonly Branch _b01 = Branch.Create("B01", "Norte", null, isExternal: true);
    private readonly ExternalEmployeeDeviceIds _ids;

    public ExternalEmployeeDeviceIdsTests()
    {
        _ids = new ExternalEmployeeDeviceIds(new[] { _b01, Branch.Create("L01", "Matriz", null) });
    }

    [Theory]
    [InlineData("B01123", "123")]
    [InlineData("b01123", "123")]
    [InlineData(" B01A7 ", "A7")]
    public void AnIdWithAnExternalCode_GivesItsBranchAndNumber(string deviceUserId, string number)
    {
        _ids.TryParse(deviceUserId, out var branchId, out var employeeNumber).Should().BeTrue();
        branchId.Should().Be(_b01.Id);
        employeeNumber.Should().Be(number);
    }

    [Theory]
    [InlineData("L01123")] // sucursal local
    [InlineData("475")]
    [InlineData("B01")]    // sin número
    [InlineData("")]
    public void OtherIds_AreNotExternal(string deviceUserId)
    {
        _ids.TryParse(deviceUserId, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void DeviceUserIdOf_UsesTheBranchCodeAsStored()
    {
        _ids.DeviceUserIdOf(ExternalEmployee.Create(_b01.Id, "123", "Ana", "López")).Should().Be("B01123");
    }
}
