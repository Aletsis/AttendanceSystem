using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Commands.SendBranchEmployeesToDevice;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

/// <summary>
/// Alta de un reloj nuevo: todos los empleados activos de la sucursal en una sola conexión con el reloj apartado; uno
/// que el reloj rechaza no detiene a los demás.
/// </summary>
public class SendBranchEmployeesToDeviceCommandHandlerTests
{
    private readonly Mock<IDeviceClientFactory> _clients = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IExternalEmployeeRepository> _externalEmployees = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IDeviceLockService> _locks = new();
    private readonly List<string> _lockedDevices = new();
    private readonly List<DeviceUserDto> _sent = new();

    public SendBranchEmployeesToDeviceCommandHandlerTests()
    {
        _clients.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(_client.Object);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client.Setup(c => c.SetUserAsync(It.IsAny<DeviceUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceUserDto, CancellationToken>((u, _) => _sent.Add(u))
            .ReturnsAsync(true);
        _locks.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((id, action, _) => { _lockedDevices.Add(id); return action(); });
        _externalEmployees.Setup(r => r.GetByBranchAsync(It.IsAny<BranchId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalEmployee>());
    }

    private SendBranchEmployeesToDeviceCommandHandler Handler() => new(
        _clients.Object, _devices.Object, _employees.Object, _externalEmployees.Object, _branches.Object, _locks.Object,
        NullLogger<SendBranchEmployeesToDeviceCommandHandler>.Instance);

    private Device AddDevice()
    {
        var device = Device.Create("DEV-NEW", "Reloj nuevo", "10.0.0.9", 4370, serialNumber: "SN-NEW");
        _devices.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        return device;
    }

    private Branch AddBranch(bool isExternal = false)
    {
        var branch = Branch.Create(isExternal ? "E01" : "M01", "Sucursal", null, isExternal);
        _branches.Setup(r => r.GetByIdAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        return branch;
    }

    private Employee NewEmployee(string id, Branch branch) =>
        Employee.Create(EmployeeId.From(id), "E", id, null, null, new DateTime(2024, 1, 1), Gender.Male,
            branch.Id, DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino);

    private void WithEmployees(Branch branch, params Employee[] employees) =>
        _employees.Setup(r => r.GetByBranchAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employees);

    [Fact]
    public async Task ActiveEmployees_AreSentOverOneConnection_SkippingTerminated()
    {
        var device = AddDevice();
        var branch = AddBranch();
        var terminated = NewEmployee("103", branch);
        terminated.SetStatus(EmployeeStatus.Baja);
        WithEmployees(branch, NewEmployee("101", branch), NewEmployee("102", branch), terminated);

        var result = await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new SendBranchEmployeesToDeviceResult(2, 2, Array.Empty<string>()));
        _sent.Select(u => u.UserId).Should().Equal("101", "102");
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        _lockedDevices.Should().Equal(device.Id.Value);
    }

    [Fact]
    public async Task AnExternalBranch_SendsItsExternalEmployees_WithTheBranchPrefix()
    {
        var device = AddDevice();
        var branch = AddBranch(isExternal: true);
        WithEmployees(branch, NewEmployee("201", branch));
        _externalEmployees.Setup(r => r.GetByBranchAsync(branch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExternalEmployee.Create(branch.Id, "55", "Ana", "Ruiz") });

        var result = await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value), CancellationToken.None);

        result.Value.Sent.Should().Be(2);
        _sent.Select(u => u.UserId).Should().Equal("E01201", "E0155");
    }

    [Fact]
    public async Task ARejectedEmployee_DoesNotStopTheRest()
    {
        var device = AddDevice();
        var branch = AddBranch();
        WithEmployees(branch, NewEmployee("301", branch), NewEmployee("302", branch));
        _client.Setup(c => c.SetUserAsync(It.Is<DeviceUserDto>(u => u.UserId == "301"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rechazado"));

        var result = await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sent.Should().Be(1);
        result.Value.Failed.Should().ContainSingle().Which.Should().Contain("301");
    }

    [Fact]
    public async Task AnUnreachableClock_Fails()
    {
        var device = AddDevice();
        var branch = AddBranch();
        WithEmployees(branch, NewEmployee("401", branch));
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _client.Verify(c => c.SetUserAsync(It.IsAny<DeviceUserDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Progress_IsReportedForEachEmployee()
    {
        var device = AddDevice();
        var branch = AddBranch();
        WithEmployees(branch, NewEmployee("501", branch), NewEmployee("502", branch));
        var progress = new RecordingProgress();

        await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value, progress), CancellationToken.None);

        progress.Reports.Select(p => p.Done).Should().Equal(0, 0, 0, 1);
        progress.Reports.Should().OnlyContain(p => p.Total == 2);
        progress.Reports[2].Message.Should().Contain("501");
        progress.Reports[3].Message.Should().Contain("502");
    }

    [Fact]
    public async Task Cancelling_StopsBetweenEmployees_AndReturnsWhatWasSent()
    {
        var device = AddDevice();
        var branch = AddBranch();
        WithEmployees(branch, NewEmployee("601", branch), NewEmployee("602", branch), NewEmployee("603", branch));
        using var cancellation = new CancellationTokenSource();
        _client.Setup(c => c.SetUserAsync(It.IsAny<DeviceUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceUserDto, CancellationToken>((u, token) =>
            {
                token.CanBeCanceled.Should().BeFalse("cortar a media escritura dejaría al reloj deshabilitado");
                _sent.Add(u);
                cancellation.Cancel();
            })
            .ReturnsAsync(true);

        var result = await Handler().Handle(new SendBranchEmployeesToDeviceCommand(device.Id.Value, branch.Id.Value), cancellation.Token);

        result.Value.Should().BeEquivalentTo(new SendBranchEmployeesToDeviceResult(3, 1, Array.Empty<string>(), Cancelled: true));
        _sent.Select(u => u.UserId).Should().Equal("601");
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Síncrono, a diferencia de <see cref="Progress{T}"/>, que publica en el pool de hilos.</summary>
    private sealed class RecordingProgress : IProgress<SendBranchEmployeesProgress>
    {
        public List<SendBranchEmployeesProgress> Reports { get; } = new();
        public void Report(SendBranchEmployeesProgress value) => Reports.Add(value);
    }
}
