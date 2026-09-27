using AttendanceSystem.Application.Abstractions;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Blazor.Server.Services;

/// <summary>
/// Controla el ciclo de vida, pausa y reanudación del servidor de Hangfire durante operaciones críticas como restauraciones de base de datos.
/// Implementa IHostedService para ser administrado por el host de la aplicación como Singleton, evitando instancias transitorias no controladas.
/// </summary>
public class HangfireJobControlService : IBackgroundJobControlService, IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireJobControlService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private BackgroundJobServer? _server;
    private bool _isPaused;
    private bool _isDisposed;

    public HangfireJobControlService(
        IServiceProvider serviceProvider,
        ILogger<HangfireJobControlService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lock.Wait(cancellationToken);
        try
        {
            if (!_isPaused && !_isDisposed)
            {
                StartServerUnsafe();
            }
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _isDisposed = true;
            await StopServerUnsafeAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task PauseBackgroundJobsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _isPaused = true;
            if (_server != null)
            {
                _logger.LogInformation("⏸️ Pausando el servidor de Hangfire para evitar accesos concurrentes a la base de datos...");
                await StopServerUnsafeAsync(cancellationToken);
                _logger.LogInformation("✅ Servidor de Hangfire pausado y detenido exitosamente.");
            }
            else
            {
                _logger.LogInformation("ℹ️ El servidor de Hangfire ya se encontraba detenido.");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ResumeBackgroundJobsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _isPaused = false;
            if (!_isDisposed)
            {
                _logger.LogInformation("▶️ Reanudando el servidor de Hangfire tras la operación en la base de datos...");
                StartServerUnsafe();
                _logger.LogInformation("✅ Servidor de Hangfire reanudado exitosamente.");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void StartServerUnsafe()
    {
        if (_server != null || _isPaused || _isDisposed)
        {
            return;
        }

        try
        {
            var storage = _serviceProvider.GetRequiredService<JobStorage>();
            var options = _serviceProvider.GetService<BackgroundJobServerOptions>() ?? new BackgroundJobServerOptions();
            var additionalProcesses = _serviceProvider.GetServices<IBackgroundProcess>();

            options.Activator ??= _serviceProvider.GetService<JobActivator>();

            _logger.LogInformation("🚀 Iniciando servidor de Hangfire...");
            _server = new BackgroundJobServer(options, storage, additionalProcesses);
            _logger.LogInformation("✅ Servidor de Hangfire iniciado exitosamente.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error al iniciar el servidor de Hangfire.");
        }
    }

    private async Task StopServerUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_server == null)
        {
            return;
        }

        try
        {
            _server.SendStop();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _server.WaitForShutdownAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("⚠️ Tiempo límite alcanzado esperando que el servidor de Hangfire se detuviera por completo.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Advertencia al detener el servidor de Hangfire.");
        }
        finally
        {
            try
            {
                _server.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Advertencia al liberar recursos del servidor de Hangfire.");
            }
            _server = null;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        try
        {
            _server?.Dispose();
        }
        catch { }
        _server = null;
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
