namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Contrato de Unidad de Trabajo que coordina la persistencia y transaccionalidad de operaciones.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Guarda todos los cambios pendientes en la base de datos de manera atómica.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia una transacción explícita en la base de datos.
    /// </summary>
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma la transacción activa.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revierte la transacción activa.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarta el seguimiento de cambios tras un fallo (F03-07) para no reutilizar un contexto con
    /// entidades fallidas aún pendientes. Las entidades leídas antes dejan de rastrearse.
    /// </summary>
    void DiscardChanges();
}
