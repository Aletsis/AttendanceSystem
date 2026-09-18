using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Backup.Queries;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Devices.Queries.GetActiveDevices;
using AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;
using AttendanceSystem.Application.Features.DownloadLogs.Queries.GetDownloadLogs;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Application.Features.Users;
using AttendanceSystem.Application.Features.Users.Queries.GetUsers;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class AdministrationPageTests : BlazorTestBase
{
    [Fact]
    public void DevicesPage_ShouldRenderWithoutException()
    {
        var devices = new List<DeviceDto>
        {
            new() { DeviceId = "DEV01", Name = "Biométrico Entrada", IpAddress = "192.168.1.100", Port = 4370, IsActive = true }
        };

        SenderMock.Setup(s => s.Send(It.IsAny<GetAllDevicesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DeviceDto>>.Success(devices));

        var cut = RenderComponent<Devices>();

        cut.Markup.Should().Contain("Dispositivos");
        cut.Markup.Should().Contain("Nuevo Dispositivo");
    }

    [Fact]
    public void ShiftsPage_ShouldRenderTableWithShifts()
    {
        var shifts = new List<ShiftDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Turno Matutino", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(17, 0, 0), WorkHours = TimeSpan.FromHours(8) }
        };

        SenderMock.Setup(s => s.Send(It.IsAny<GetShiftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<ShiftDto>>.Success(shifts));

        var cut = RenderComponent<Shifts>();

        cut.Markup.Should().Contain("Horarios y Turnos");
        cut.Markup.Should().Contain("Turno Matutino");
    }

    [Fact]
    public void UsersPage_ShouldRenderUserGrid()
    {
        var users = new List<UserDto>
        {
            new(Guid.NewGuid().ToString(), "admin", "admin@empresa.com", "Administrador Principal", true, new List<string> { "Administrador" })
        };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var cut = RenderComponent<Users>();

        cut.Markup.Should().Contain("Gestión de Usuarios");
        cut.Markup.Should().Contain("admin");
    }

    [Fact]
    public void SettingsPage_ShouldRenderTabsAndConfigurationForm()
    {
        var config = new SystemConfigurationDto
        {
            CompanyName = "Mi Empresa S.A.",
            LateToleranceMinutes = TimeSpan.FromMinutes(10),
            IsAutoBackupEnabled = true
        };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(config));

        var cut = RenderComponent<Settings>();

        cut.Markup.Should().Contain("Configuración del Sistema");
        cut.Markup.Should().Contain("Identidad Corporativa");
    }

    [Fact]
    public void DownloadPage_ShouldRenderDownloadControls()
    {
        var activeDevices = new List<DeviceDto>
        {
            new() { DeviceId = "DEV01", Name = "Reloj 1", IpAddress = "192.168.1.50", Port = 4370, IsActive = true }
        };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetActiveDevicesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DeviceDto>>.Success(activeDevices));

        MediatorMock.Setup(m => m.Send(It.IsAny<GetDownloadLogsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DownloadLog>>.Success(new List<DownloadLog>()));

        var cut = RenderComponent<Download>();

        cut.Markup.Should().Contain("Gestión de Descargas");
        cut.Markup.Should().Contain("Nueva Descarga");
    }

    [Fact]
    public void BackupPage_ShouldRenderBackupControls()
    {
        var config = new SystemConfigurationDto { BackupDirectory = "/var/backups" };

        MediatorMock.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(config));

        MediatorMock.Setup(m => m.Send(It.IsAny<GetBackupsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BackupDto>());

        var cut = RenderComponent<Backup>();

        cut.Markup.Should().Contain("Respaldo y Restauración");
    }
}
