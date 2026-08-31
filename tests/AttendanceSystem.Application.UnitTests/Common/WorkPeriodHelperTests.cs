using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Common;

public class WorkPeriodHelperTests
{
    [Fact]
    public void GetPeriodDates_MonthlyMode_ShouldReturnStartAndEndOfMonth()
    {
        // Arrange
        var config = new SystemConfigurationDto(
            CompanyName: "Test Corp",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            WorkPeriodMode: WorkPeriodMode.Monthly,
            MonthlyStartDay: 1);

        // Act (Agosto 2026 -> 1 al 31)
        var (start, end) = config.GetPeriodDates(2026, 8);

        // Assert
        start.Should().Be(new DateTime(2026, 8, 1));
        end.Should().Be(new DateTime(2026, 8, 31));
    }

    [Fact]
    public void GetPeriodDates_FortnightlyMode_FirstHalf_ShouldReturnFirstToFifteenth()
    {
        // Arrange
        var config = new SystemConfigurationDto(
            CompanyName: "Test Corp",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            WorkPeriodMode: WorkPeriodMode.Fortnightly,
            FortnightFirstDay: 1,
            FortnightSecondDay: 16);

        // Act (Agosto Q1 -> PeriodNum = (8 - 1) * 2 + 1 = 15)
        var (start, end) = config.GetPeriodDates(2026, 15);

        // Assert
        start.Should().Be(new DateTime(2026, 8, 1));
        end.Should().Be(new DateTime(2026, 8, 15));
    }

    [Fact]
    public void GetPeriodDates_FortnightlyMode_SecondHalf_ShouldReturnSixteenthToEndOfMonth()
    {
        // Arrange
        var config = new SystemConfigurationDto(
            CompanyName: "Test Corp",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            WorkPeriodMode: WorkPeriodMode.Fortnightly,
            FortnightFirstDay: 1,
            FortnightSecondDay: 16);

        // Act (Agosto Q2 -> PeriodNum = 16)
        var (start, end) = config.GetPeriodDates(2026, 16);

        // Assert
        start.Should().Be(new DateTime(2026, 8, 16));
        end.Should().Be(new DateTime(2026, 8, 31));
    }

    [Fact]
    public void GetAvailablePeriods_FortnightlyMode_ShouldReturn24Periods()
    {
        // Arrange
        var config = new SystemConfigurationDto(
            CompanyName: "Test Corp",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            WorkPeriodMode: WorkPeriodMode.Fortnightly,
            FortnightFirstDay: 1,
            FortnightSecondDay: 16);

        // Act
        var periods = config.GetAvailablePeriods(2026);

        // Assert
        periods.Should().HaveCount(24);
    }

    [Fact]
    public void GetAvailablePeriods_MonthlyMode_ShouldReturn12Periods()
    {
        // Arrange
        var config = new SystemConfigurationDto(
            CompanyName: "Test Corp",
            CompanyLogo: null,
            LateToleranceMinutes: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: false,
            IsAutoDownloadEnabled: false,
            AutoDownloadTime: null,
            AutoDownloadOnlyToday: false,
            WorkPeriodMode: WorkPeriodMode.Monthly,
            MonthlyStartDay: 1);

        // Act
        var periods = config.GetAvailablePeriods(2026);

        // Assert
        periods.Should().HaveCount(12);
    }
}
