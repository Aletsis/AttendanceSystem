using AttendanceSystem.Infrastructure.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Logging;

/// <summary>
/// Deploy/appsettings.Web.Production.template.json es la configuración que el instalador escribe para el sitio web. El
/// instalador no copia ningún appsettings.json base (no está en el repositorio y setup_script.iss lo excluye), así que la
/// plantilla sola debe bastar para que Serilog se configure: si falla, el host no se construye y el sitio no arranca
/// (v2.3.0-beta.1 fijaba Serilog:WriteTo:3:Args sin un cuarto sink que completar).
/// </summary>
public class WebProductionTemplateLoggingTests
{
    private static string Repo(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AttendanceSystem.sln"))) root = Path.GetDirectoryName(root)!;
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private static readonly string TemplatePath = Repo("Deploy", "appsettings.Web.Production.template.json");

    private static Action ConfigureLogging(IConfiguration configuration) => () =>
    {
        using var logger = new LoggerConfiguration().ConfigureAttendanceLogging(configuration).CreateLogger();
        logger.Information("prueba de la plantilla del instalador");
    };

    [Fact]
    public void PlantillaSola_ConfiguraSerilog()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(TemplatePath).Build();

        ConfigureLogging(configuration).Should().NotThrow();
    }

    [Fact]
    public void PlantillaSobreElEjemploDelRepositorio_ConfiguraSerilog()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Repo("src", "Presentation", "AttendanceSystem.Blazor.Server", "appsettings.example.json"))
            .AddJsonFile(TemplatePath)
            .Build();

        ConfigureLogging(configuration).Should().NotThrow();
    }

    [Fact]
    public void Plantilla_CadaSinkDeclaraSuNombre()
    {
        // Un sink por índice sin "Name" sólo completa uno de un appsettings.json base, que el instalador no trae.
        var writeTo = new ConfigurationBuilder().AddJsonFile(TemplatePath).Build().GetSection("Serilog:WriteTo");

        // Sin sinks también vale (los archivos de log los agrega LoggingConfiguration); AllSatisfy fallaría con la lista vacía.
        foreach (var sink in writeTo.GetChildren())
            sink["Name"].Should().NotBeNullOrWhiteSpace($"Serilog:WriteTo:{sink.Key} necesita su Name");
    }
}
