namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Servicio para pausar y reanudar los servidores y tareas de fondo (como Hangfire) durante operaciones críticas.
/// </summary>
public interface IBackgroundJobControlService
{
    /// <summary>
    /// Pausa o detiene temporalmente la ejecución y heartbeats de trabajos en segundo plano.
    /// </summary>
    Task PauseBackgroundJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reanuda la ejecución de trabajos en segundo plano.
    /// </summary>
    Task ResumeBackgroundJobsAsync(CancellationToken cancellationToken = default);
}
