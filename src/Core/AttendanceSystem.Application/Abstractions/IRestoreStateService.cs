using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Servicio para gestionar el estado de restauración y exclusión de mantenimiento de la base de datos.
/// Permite aislar el sistema, drenar escritores y coordinar la exclusión entre procesos.
/// </summary>
public interface IRestoreStateService
{
    /// <summary>
    /// Indica si hay una restauración o mantenimiento de base de datos en curso.
    /// </summary>
    bool IsRestoreInProgress { get; }

    /// <summary>
    /// Estado detallado actual de mantenimiento.
    /// </summary>
    MaintenanceState CurrentState { get; }

    /// <summary>
    /// Identificador del proceso o usuario propietario del bloqueo de mantenimiento actual.
    /// </summary>
    string? CurrentOwner { get; }

    /// <summary>
    /// Indica si se permiten operaciones de escritura en este momento.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Número actual de escritores activos en curso.
    /// </summary>
    int ActiveWritersCount { get; }

    /// <summary>
    /// Intenta adquirir el bloqueo exclusivo de restauración para un propietario dado.
    /// Rechaza la adquisición si ya existe otra restauración en curso.
    /// </summary>
    Task<bool> TryAcquireRestoreLockAsync(string ownerId, string description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Libera el bloqueo de restauración adquirido por el propietario.
    /// </summary>
    Task ReleaseRestoreLockAsync(string ownerId, bool success, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transiciona el estado de mantenimiento notificando a los componentes suscritos.
    /// </summary>
    Task TransitionStateAsync(MaintenanceState newState, string? details = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra el inicio de una operación de escritura activa. Devuelve un IDisposable para desregistrar al finalizar.
    /// Lanza DatabaseMaintenanceException si el sistema no permite escrituras.
    /// </summary>
    IDisposable RegisterActiveWriter();

    /// <summary>
    /// Espera a que finalicen todas las operaciones de escritura activas hasta un tiempo límite.
    /// </summary>
    Task DrainActiveWritersAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa el modo de restauración/mantenimiento (compatibilidad sincrónica).
    /// </summary>
    void EnterRestoreMode();

    /// <summary>
    /// Desactiva el modo de restauración/mantenimiento (compatibilidad sincrónica).
    /// </summary>
    void ExitRestoreMode();
}
