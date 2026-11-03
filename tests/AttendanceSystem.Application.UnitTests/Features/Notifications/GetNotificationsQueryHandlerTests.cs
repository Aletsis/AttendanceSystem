using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Features.Notifications;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Notifications;

/// <summary>La campana muestra las alertas abiertas que cada rol puede atender, cada una con la vista donde se atiende.</summary>
public class GetNotificationsQueryHandlerTests
{
    private readonly List<SystemAlert> _alerts = new();

    private async Task<IReadOnlyList<NotificationDto>> AsAsync(params string[] roles)
    {
        var repository = new Mock<ISystemAlertRepository>();
        repository.Setup(r => r.GetActiveAlertsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _alerts.Where(a => !a.IsResolved).ToList());
        return await new GetNotificationsQueryHandler(repository.Object).Handle(new GetNotificationsQuery(roles), CancellationToken.None);
    }

    private SystemAlert Add(AlertType type, string? key, string message = "mensaje")
    {
        var alert = SystemAlert.Create(type, key, message);
        _alerts.Add(alert);
        return alert;
    }

    [Theory]
    [InlineData(AlertType.DeviceOffline, "R1", "/devices")]
    [InlineData(AlertType.DeviceMemoryFull, "R1", "/devices")]
    [InlineData(AlertType.DeviceClockDrift, "R1", "/devices/clock-time")]
    [InlineData(AlertType.PunchQuarantined, "R1", "/devices/clock-time")]
    [InlineData(AlertType.CriticalPositionAbsence, "EMP_20261014", "/attendance/logs")]
    [InlineData(AlertType.SystemError, "tarea:automated-database-backup:20261014", "/backup")]
    [InlineData(AlertType.SystemError, "tarea:automated-daily-report:20261014", "/settings")]
    [InlineData(AlertType.SystemError, "tarea:download-attendance-daily:20261014", "/attendance/download")]
    [InlineData(AlertType.SystemError, "relojes:20261014", "/attendance/download")]
    [InlineData(AlertType.SystemError, "tarea:daily-attendance-calculation:20261014", "/attendance/calculation")]
    [InlineData(AlertType.SystemError, "tarea:auto-close-periods:20261014", "/attendance/periods")]
    [InlineData(AlertType.SystemError, "cierre:20261005", "/attendance/periods")]
    [InlineData(AlertType.SystemError, "otra-cosa", "/settings")]
    public async Task EachAlert_LeadsToTheViewWhereItIsHandled(AlertType type, string key, string href)
    {
        Add(type, key);

        (await AsAsync(AppRoles.Administrator)).Should().ContainSingle().Which.Href.Should().Be(href);
    }

    [Fact]
    public async Task EachRole_SeesOnlyWhatItCanOpen()
    {
        Add(AlertType.DeviceOffline, "R1");
        Add(AlertType.SystemError, "tarea:automated-database-backup:20261014");
        Add(AlertType.SystemError, "cierre:20261005");
        Add(AlertType.CriticalPositionAbsence, "EMP_20261014");

        (await AsAsync(AppRoles.Administrator)).Should().HaveCount(4);
        (await AsAsync(AppRoles.HumanResources)).Select(n => n.Href).Should().BeEquivalentTo("/attendance/periods", "/attendance/logs");
        (await AsAsync(AppRoles.User)).Select(n => n.Href).Should().BeEquivalentTo("/attendance/logs");
        (await AsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ScheduleProblems_AreOneNotification_LeadingToTheDashboardList()
    {
        Add(AlertType.ScheduleIntegrity, "horario:E1");
        Add(AlertType.ScheduleIntegrity, "roster:E2");
        Add(AlertType.ScheduleIntegrity, "mixto:S1");

        var notification = (await AsAsync(AppRoles.User)).Should().ContainSingle().Subject;
        notification.Href.Should().Be("/");
        notification.Message.Should().Contain("3");
    }

    [Fact]
    public async Task ResolvedAlerts_AndPastCriticalAbsences_AreNotShown()
    {
        Add(AlertType.DeviceOffline, "R1").Resolve();
        var old = Add(AlertType.CriticalPositionAbsence, "EMP_20261001");
        typeof(SystemAlert).GetProperty(nameof(SystemAlert.Timestamp))!.SetValue(old, DateTime.UtcNow.AddDays(-2));

        (await AsAsync(AppRoles.Administrator)).Should().BeEmpty();
    }

    [Fact]
    public async Task ErrorsComeFirst_ThenTheNewest()
    {
        var warning = Add(AlertType.DeviceMemoryFull, "R1");
        Add(AlertType.DeviceOffline, "R2");

        var list = await AsAsync(AppRoles.Administrator);

        list[0].Severity.Should().Be(NotificationSeverity.Error);
        list[1].Id.Should().Be(warning.Id.Value.ToString());
    }
}
