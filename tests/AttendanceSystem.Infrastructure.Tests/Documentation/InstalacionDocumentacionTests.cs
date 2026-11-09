using AttendanceSystem.Infrastructure.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Documentation;

/// <summary>
/// Docs/tecnico/05-instalacion-configuracion.md: la configuración que escribe el instalador para el sitio web. Las pruebas
/// fijan el comportamiento actual; si cambia, el documento debe actualizarse.
/// </summary>
public class InstalacionDocumentacionTests
{
    private static string Repo(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AttendanceSystem.sln"))) root = Path.GetDirectoryName(root)!;
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private static Action ConfigureLogging(IConfiguration configuration) => () =>
    {
        using var logger = new LoggerConfiguration().ConfigureAttendanceLogging(configuration).CreateLogger();
    };

    [Fact]
    public void I01_PlantillaDelSitioSinAppsettingsBase_SerilogNoPuedeConfigurarse()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Repo("Deploy", "appsettings.Web.Production.template.json"))
            .Build();

        // La plantilla sólo fija Args de Serilog:WriteTo:3, que debe existir (con su Name) en appsettings.json.
        ConfigureLogging(configuration).Should().Throw<InvalidOperationException>()
            .WithMessage("*Serilog:WriteTo:3:Name*");
    }

    [Fact]
    public void I02_PlantillaSobreElEjemploDelRepositorio_TambienFalla_PorqueElEjemploTieneUnSoloSink()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Repo("src", "Presentation", "AttendanceSystem.Blazor.Server", "appsettings.example.json"))
            .AddJsonFile(Repo("Deploy", "appsettings.Web.Production.template.json"))
            .Build();

        ConfigureLogging(configuration).Should().Throw<InvalidOperationException>()
            .WithMessage("*Serilog:WriteTo:3:Name*");
    }

    [Fact]
    public void I03_ElEjemploDelRepositorioSolo_SiConfigura()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Repo("src", "Presentation", "AttendanceSystem.Blazor.Server", "appsettings.example.json"))
            .Build();

        ConfigureLogging(configuration).Should().NotThrow();
    }
}
