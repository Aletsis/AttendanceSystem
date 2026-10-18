using System;

namespace AttendanceSystem.Infrastructure.Exceptions;

/// <summary>
/// F06-P05 / H19: Excepción lanzada cuando un trabajo en segundo plano (Hangfire) termina con un resultado de negocio fallido.
/// Permite que Hangfire registre el job como fallido (Failed state), active reintentos configurados y muestre el error en el Dashboard.
/// </summary>
public class JobExecutionFailedException : Exception
{
    public string JobName { get; }

    public JobExecutionFailedException(string jobName, string? message)
        : base($"Job '{jobName}' falló en su ejecución de negocio: {message ?? "Error desconocido"}")
    {
        JobName = jobName;
    }

    public JobExecutionFailedException(string jobName, string? message, Exception? innerException)
        : base($"Job '{jobName}' falló en su ejecución de negocio: {message ?? "Error desconocido"}", innerException)
    {
        JobName = jobName;
    }
}
