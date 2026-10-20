using System;
using System.Threading.Tasks;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Fixture de prueba para PostgreSQL real que crea una base de datos desechable y aislada
/// para cada ejecución, aplica todas las migraciones reales y elimina la base de datos al finalizar.
/// F00-03: Permite validar restricciones únicas, índices, transacciones y tipos reales de PostgreSQL.
/// </summary>
public class PostgresTestDatabaseFixture : IAsyncLifetime
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;
    private readonly string _testConnectionString;
    private bool _isInitialized;

    public string DatabaseName => _databaseName;
    public string ConnectionString => _testConnectionString;
    public bool IsAvailable { get; private set; }

    public PostgresTestDatabaseFixture()
    {
        _databaseName = $"attendance_test_{Guid.NewGuid():N}";

        // Cadena administrativa configurada por env var o valores locales conocidos
        var envConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (!string.IsNullOrWhiteSpace(envConnection))
        {
            var builder = new NpgsqlConnectionStringBuilder(envConnection);
            builder.Database = "postgres";
            _adminConnectionString = builder.ConnectionString;

            builder.Database = _databaseName;
            _testConnectionString = builder.ConnectionString;
        }
        else
        {
            // Fallback con credenciales estándar de desarrollo local
            _adminConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=attendanceuser;Password=AttendancePass;";
            _testConnectionString = $"Host=localhost;Port=5432;Database={_databaseName};Username=attendanceuser;Password=AttendancePass;";
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            // 1. Probar conexión al servidor PostgreSQL
            await using var adminConn = new NpgsqlConnection(_adminConnectionString);
            try
            {
                await adminConn.OpenAsync();
            }
            catch
            {
                // Intentar fallback a usuario 'postgres' si attendanceuser no conecta
                var fallbackBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
                {
                    Username = "postgres",
                    Password = "postgres123"
                };
                adminConn.ConnectionString = fallbackBuilder.ConnectionString;
                await adminConn.OpenAsync();
            }

            // 2. Crear base de datos aislada
            await using (var createDbCmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\";", adminConn))
            {
                await createDbCmd.ExecuteNonQueryAsync();
            }

            // 3. Aplicar todas las migraciones reales de EF Core sobre la base de datos creada
            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();

            _isInitialized = true;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // C06: en CI la suite relacional es obligatoria; omitirla en silencio ocultaría regresiones.
            if (string.Equals(Environment.GetEnvironmentVariable("REQUIRE_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"REQUIRE_POSTGRES_TESTS=true pero PostgreSQL no está disponible: {ex.Message}", ex);
            }

            Console.WriteLine($"[AVISO] No se pudo inicializar la base de datos PostgreSQL de prueba: {ex.Message}");
            IsAvailable = false;
        }
    }

    public AttendanceDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AttendanceDbContext>();
        optionsBuilder.UseNpgsql(_testConnectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(AttendanceDbContext).Assembly.FullName);
        });

        var publisherMock = new Moq.Mock<MediatR.IPublisher>();
        return new AttendanceDbContext(optionsBuilder.Options, publisherMock.Object);
    }

    public async Task DisposeAsync()
    {
        if (!_isInitialized) return;

        try
        {
            await using var adminConn = new NpgsqlConnection(_adminConnectionString);
            try
            {
                await adminConn.OpenAsync();
            }
            catch
            {
                var fallbackBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
                {
                    Username = "postgres",
                    Password = "postgres123"
                };
                adminConn.ConnectionString = fallbackBuilder.ConnectionString;
                await adminConn.OpenAsync();
            }

            // Terminar conexiones activas hacia la base de datos de prueba
            await using (var terminateCmd = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}';", adminConn))
            {
                await terminateCmd.ExecuteNonQueryAsync();
            }

            // Eliminar base de datos desechable
            await using (var dropDbCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\";", adminConn))
            {
                await dropDbCmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVISO] Error al eliminar base de datos de prueba {_databaseName}: {ex.Message}");
        }
    }
}
