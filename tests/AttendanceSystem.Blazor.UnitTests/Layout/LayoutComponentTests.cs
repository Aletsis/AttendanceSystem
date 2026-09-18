using AttendanceSystem.Blazor.Server.Components.Layout;
using AttendanceSystem.Blazor.UnitTests.Common;

namespace AttendanceSystem.Blazor.UnitTests.Layout;

public class LayoutComponentTests : BlazorTestBase
{
    [Fact]
    public void NavMenu_ShouldRenderNavigationLinks()
    {
        var cut = RenderComponent<NavMenu>();

        cut.Markup.Should().Contain("Inicio");
        cut.Markup.Should().Contain("Gestión de personal");
        cut.Markup.Should().Contain("Dispositivos y descargas");
        cut.Markup.Should().Contain("Reportes y análisis");
        cut.Markup.Should().Contain("Configuración");
        cut.Markup.Should().Contain("Empleados");
        cut.Markup.Should().Contain("Sucursales");
        cut.Markup.Should().Contain("Departamentos");
        cut.Markup.Should().Contain("Horarios");
        cut.Markup.Should().Contain("Dispositivos");
    }

    [Fact]
    public void LoginLayout_ShouldRenderBodyContent()
    {
        var cut = RenderComponent<LoginLayout>(parameters => parameters
            .Add(p => p.Body, (Microsoft.AspNetCore.Components.RenderFragment)(builder =>
            {
                builder.AddContent(0, "Contenido de Login");
            }))
        );

        cut.Markup.Should().Contain("Contenido de Login");
    }

    [Fact]
    public void MainLayout_ShouldRenderAppBarAndProviders()
    {
        var cut = RenderComponent<MainLayout>(parameters => parameters
            .Add(p => p.Body, (Microsoft.AspNetCore.Components.RenderFragment)(builder =>
            {
                builder.AddContent(0, "Contenido Principal");
            }))
        );

        cut.Markup.Should().Contain("Asistencia");
        cut.Markup.Should().Contain("Contenido Principal");
    }
}
