namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Servicio para gestionar el estado de restauración de la base de datos en la aplicación.
/// Permite aislar el sistema y notificar a los clientes y tareas concurrentes.
/// </summary>
public interface IRestoreStateService
{
    /// <summary>
    /// Indica si hay una restauración de base de datos en curso.
    /// </summary>
    bool IsRestoreInProgress { get; }

    /// <summary>
    /// Activa el modo de restauración/mantenimiento.
    /// </summary>
    void EnterRestoreMode();

    /// <summary>
    /// Desactiva el modo de restauración/mantenimiento.
    /// </summary>
    void ExitRestoreMode();
}
