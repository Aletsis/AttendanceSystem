using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Implementación No-Op de IBackgroundJobControlService cuando no se requiere pausar tareas en segundo plano (e.g. clientes WPF o tests).
/// </summary>
public class NullBackgroundJobControlService : IBackgroundJobControlService
{
    public Task PauseBackgroundJobsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ResumeBackgroundJobsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
