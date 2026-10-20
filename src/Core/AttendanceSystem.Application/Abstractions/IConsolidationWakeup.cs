namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Aviso en proceso para que el consumidor de consolidación revise pendientes sin esperar su sondeo.
/// Perder el aviso no pierde trabajo: la solicitud está en la base de datos (F04-05).
/// Varios avisos seguidos se agrupan en una sola revisión.
/// </summary>
public interface IConsolidationWakeup
{
    void Signal();
}

public sealed class NoOpConsolidationWakeup : IConsolidationWakeup
{
    public static readonly IConsolidationWakeup Instance = new NoOpConsolidationWakeup();
    public void Signal() { }
}
