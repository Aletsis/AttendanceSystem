using AttendanceSystem.Application.Features.Notifications;
using AttendanceSystem.Blazor.Server.Components.Layout;
using AttendanceSystem.Blazor.UnitTests.Common;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using MudBlazor;

namespace AttendanceSystem.Blazor.UnitTests.Layout;

public class NotificationBellTests : BlazorTestBase
{
    private IReadOnlyCollection<string>? _askedRoles;

    private void Notifications(params NotificationDto[] notifications) =>
        MediatorMock.Setup(m => m.Send(It.IsAny<GetNotificationsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<IReadOnlyList<NotificationDto>>, CancellationToken>((q, _) => _askedRoles = ((GetNotificationsQuery)q).Roles)
            .ReturnsAsync(notifications);

    private static NotificationDto Notification(string title, string href, NotificationSeverity severity = NotificationSeverity.Error) =>
        new(Guid.NewGuid().ToString(), title, $"Detalle de {title}", DateTime.UtcNow.AddMinutes(-5), severity, href);

    private IRenderedFragment RenderBell() => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<NotificationBell>(1);
        builder.CloseComponent();
    });

    [Fact]
    public void TheBadge_CountsTheNotifications_AskedWithTheUsersRoles()
    {
        Notifications(Notification("Reloj desconectado", "/devices"), Notification("Cierre de periodo pospuesto", "/attendance/periods", NotificationSeverity.Warning));

        var cut = RenderBell();

        cut.Find(".mud-badge").TextContent.Should().Contain("2");
        _askedRoles.Should().BeEquivalentTo("Administrador", "Supervisor", "Usuario");
    }

    [Fact]
    public void WithoutNotifications_ThereIsNoBadge_AndTheMenuSaysSo()
    {
        Notifications();

        var cut = RenderBell();
        cut.FindAll(".mud-badge.mud-badge-dot, .mud-badge-content").Should().BeEmpty();

        cut.Find(".mud-menu button").Click();
        cut.Markup.Should().Contain("No hay notificaciones pendientes.");
    }

    [Fact]
    public void ANotification_LeadsToItsView()
    {
        Notifications(Notification("Falló el respaldo automático", "/backup"));
        var navigation = Services.GetRequiredService<FakeNavigationManager>();

        var cut = RenderBell();
        cut.Find(".mud-menu button").Click();
        cut.Markup.Should().Contain("Falló el respaldo automático").And.Contain("hace 5 min");

        cut.FindAll(".mud-menu-item").Single(e => e.TextContent.Contains("respaldo")).Click();

        cut.WaitForAssertion(() => navigation.Uri.Should().EndWith("/backup"));
    }

    [Fact]
    public void WhenTheQueryFails_TheBellStillRenders()
    {
        MediatorMock.Setup(m => m.Send(It.IsAny<GetNotificationsQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("base en mantenimiento"));

        var cut = RenderBell();

        cut.Find(".mud-menu button").Should().NotBeNull();
    }
}
