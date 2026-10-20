using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Aviso en proceso para <see cref="AttendanceConsolidationWorker"/>. Varios avisos antes de que el
/// worker despierte se agrupan en uno: una importación masiva no genera un trabajo por marcaje (C01).
/// </summary>
public sealed class ConsolidationWakeupSignal : IConsolidationWakeup
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya hay un aviso pendiente.
        }
    }

    /// <summary>Espera un aviso o el tiempo de sondeo; devuelve si hubo aviso.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
