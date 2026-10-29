using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Infrastructure.Adapters;
using AttendanceSystem.ZKTeco.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Adapters;

public class AdmsDeviceClientTests
{
    private const string Serial = "CKUH204160111";
    private static AsyncUnaryCall<T> Reply<T>(T value) => new(Task.FromResult(value),
        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Info_TriesSdkFirst_AndOnlyRequestsInfoWhenSdkUnavailable(bool sdkAvailable, bool infoAvailable)
    {
        var calls = new List<string>();
        var grpc = new Mock<ZKTecoService.ZKTecoServiceClient>(MockBehavior.Strict);
        grpc.Setup(c => c.ConnectDeviceAsync(It.IsAny<ConnectDeviceRequest>(), It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sdk-connect"))
            .Returns(() => Reply(new ConnectDeviceResponse { Success = sdkAvailable }));
        grpc.Setup(c => c.GetDeviceInfoAsync(It.IsAny<GetDeviceInfoRequest>(), It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sdk-info"))
            .Returns(() => Reply(new GetDeviceInfoResponse { Success = infoAvailable,
                DeviceInfo = new DeviceInfo { SerialNumber = Serial, FirmwareVersion = "1.0" } }));
        grpc.Setup(c => c.DisconnectDeviceAsync(It.IsAny<DisconnectDeviceRequest>(), It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Reply(new DisconnectDeviceResponse { Success = true }));

        var commands = new Mock<IAdmsCommandService>(MockBehavior.Strict);
        var response = new TaskCompletionSource<DeviceInfoDto?>();
        var expected = new DeviceInfoDto(Serial, "MiniAC Plus", "1.0", "ZKTeco", 1, 0, 1, 0, 100, 100, 100, 100);
        commands.Setup(s => s.WaitForDeviceInfoAsync(Serial, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("wait"))
            .Returns(response.Task);
        commands.Setup(s => s.EnqueueCommand(Serial, "INFO", null))
            .Callback(() => { calls.Add("INFO"); response.SetResult(expected); });

        using var provider = new ServiceCollection()
            .AddSingleton(new GrpcZKTecoDeviceClient(grpc.Object, NullLogger<GrpcZKTecoDeviceClient>.Instance))
            .BuildServiceProvider();
        var client = new AdmsDeviceClient(commands.Object, provider, NullLogger<AdmsDeviceClient>.Instance);
        client.SetDevice(Serial);
        await client.ConnectAsync("192.0.2.1", 4370);

        var result = await client.GetDeviceInfoAsync();

        result!.SerialNumber.Should().Be(Serial);
        calls.Should().Equal(infoAvailable ? new[] { "sdk-connect", "sdk-info" } :
            sdkAvailable ? new[] { "sdk-connect", "sdk-info", "wait", "INFO" } : new[] { "sdk-connect", "wait", "INFO" });
        commands.Verify(s => s.EnqueueCommand(Serial, "INFO", null), infoAvailable ? Times.Never() : Times.Once());
        grpc.Verify(c => c.ConnectDeviceAsync(It.IsAny<ConnectDeviceRequest>(), It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once());
        grpc.Verify(c => c.GetDeviceInfoAsync(It.IsAny<GetDeviceInfoRequest>(), It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), sdkAvailable ? Times.Once() : Times.Never());
        grpc.Verify(c => c.DisconnectDeviceAsync(It.IsAny<DisconnectDeviceRequest>(), It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), sdkAvailable ? Times.Once() : Times.Never());
        grpc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Info_WhenAlreadyCancelled_DoesNotContactSdkOrEnqueueCommands()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var commands = new Mock<IAdmsCommandService>(MockBehavior.Strict);
        var client = new AdmsDeviceClient(commands.Object, provider, NullLogger<AdmsDeviceClient>.Instance);
        client.SetDevice(Serial);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetDeviceInfoAsync(cancellation.Token));
        commands.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetUser_ByAdms_SendsTheNameCleanedLikeTheSdk()
    {
        var commands = new Mock<IAdmsCommandService>();
        var client = new AdmsDeviceClient(commands.Object, new ServiceCollection().BuildServiceProvider(), NullLogger<AdmsDeviceClient>.Instance);
        client.SetDevice(Serial);

        var sent = await client.SetUserAsync(new DeviceUserDto("B01123", "Carlos Eduardo Santana Gómez", "", 0, true));

        sent.Should().BeTrue();
        commands.Verify(s => s.EnqueueCommand(Serial,
            It.Is<string>(c => c.StartsWith("DATA UPDATE user\tPIN=B01123\tName=Carlos Eduardo Santana G\tPri=")), null), Times.Once);
    }
}
