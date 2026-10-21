using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class DeviceHeartbeatTrackerTests
{
    [Fact]
    public void IsDeviceOnline_WhenNoActivityRecorded_ShouldReturnFalse()
    {
        var tracker = new DeviceHeartbeatTracker();

        var isOnline = tracker.IsDeviceOnline("UNKNOWN_SN");

        isOnline.Should().BeFalse();
        tracker.GetLastActivity("UNKNOWN_SN").Should().BeNull();
    }

    [Fact]
    public void RecordActivity_WhenRecorded_ShouldReturnOnlineAndLastActivity()
    {
        var tracker = new DeviceHeartbeatTracker();
        var sn = "CKUH204160111";

        tracker.RecordActivity(sn);

        tracker.IsDeviceOnline(sn).Should().BeTrue();
        tracker.GetLastActivity(sn).Should().NotBeNull();
        tracker.GetLastActivity(sn)!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RecordActivity_CaseInsensitive_ShouldMatchUpperAndLower()
    {
        var tracker = new DeviceHeartbeatTracker();

        tracker.RecordActivity("ckuh204160111");

        tracker.IsDeviceOnline("CKUH204160111").Should().BeTrue();
        tracker.GetLastActivity("CKUH204160111").Should().NotBeNull();
    }

    [Fact]
    public void RecordActivity_WithDeviceId_ShouldMatchByBothSerialAndId()
    {
        var tracker = new DeviceHeartbeatTracker();
        var sn = "CKUH204160111";
        var id = Guid.NewGuid().ToString();

        tracker.RecordActivity(sn, id);

        tracker.IsDeviceOnline(sn).Should().BeTrue();
        tracker.IsDeviceOnline(id).Should().BeTrue();
        tracker.IsDeviceOnline(sn, id).Should().BeTrue();
    }

    [Fact]
    public void IsDeviceOnline_WhenTimeExceedsTimeout_ShouldReturnFalse()
    {
        var tracker = new DeviceHeartbeatTracker();
        var sn = "FAST_TIMEOUT_SN";

        tracker.RecordActivity(sn);
        tracker.IsDeviceOnline(sn, timeout: TimeSpan.FromSeconds(1)).Should().BeTrue();

        Thread.Sleep(1100);

        tracker.IsDeviceOnline(sn, timeout: TimeSpan.FromSeconds(1)).Should().BeFalse();
        tracker.GetLastActivity(sn).Should().NotBeNull();
    }
}
