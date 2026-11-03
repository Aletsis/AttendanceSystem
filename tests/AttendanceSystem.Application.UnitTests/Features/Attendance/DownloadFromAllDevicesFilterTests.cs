using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>Auditoría de tareas, bloque G: el reintento de la descarga automática sólo vuelve a los relojes que fallaron.</summary>
public class DownloadFromAllDevicesFilterTests
{
    [Fact]
    public async Task WithDeviceIds_OnlyThoseDevicesAreDownloaded()
    {
        var devices = new[] { Device.Create("R1", "Uno", "10.0.0.1", 4370), Device.Create("R2", "Dos", "10.0.0.2", 4370), Device.Create("R3", "Tres", "10.0.0.3", 4370) };
        var repository = new Mock<IDeviceRepository>();
        repository.Setup(r => r.GetActiveDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(devices);
        var mediator = new Mock<IMediator>();
        var asked = new List<string>();
        mediator.Setup(m => m.Send(It.IsAny<DownloadFromDeviceCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<DownloadResultDto>>, CancellationToken>((c, _) => asked.Add(((DownloadFromDeviceCommand)c).DeviceId))
            .ReturnsAsync((IRequest<Result<DownloadResultDto>> c, CancellationToken _) =>
                Result<DownloadResultDto>.Success(new DownloadResultDto(((DownloadFromDeviceCommand)c).DeviceId, 0, DateTime.UtcNow)));
        var handler = new DownloadFromAllDevicesCommandHandler(repository.Object, mediator.Object, Mock.Of<IEmployeeRepository>(),
            Mock.Of<IAttendanceConsolidationRepository>(), Mock.Of<IConsolidationWakeup>(), NullLogger<DownloadFromAllDevicesCommandHandler>.Instance);

        var result = await handler.Handle(new DownloadFromAllDevicesCommand(DeviceIds: new[] { "R1", "R3" }), CancellationToken.None);

        asked.Should().Equal("R1", "R3");
        result.Value.Select(r => r.DeviceId).Should().Equal("R1", "R3");
    }
}
