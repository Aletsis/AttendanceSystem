namespace AttendanceSystem.Application.Common;

/// <summary>
/// Estados posibles durante el ciclo de vida de mantenimiento y restauración de la base de datos.
/// </summary>
public enum MaintenanceState
{
    /// <summary>
    /// Operación normal. Lecturas y escrituras permitidas.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Preparación para entrar en mantenimiento. Se rechazan nuevas escrituras.
    /// </summary>
    Preparing = 1,

    /// <summary>
    /// Drenaje de operaciones de escritura en vuelo antes de iniciar restauración física.
    /// </summary>
    Draining = 2,

    /// <summary>
    /// Proceso de restauración de base de datos en ejecución. Escritores bloqueados.
    /// </summary>
    Restoring = 3,

    /// <summary>
    /// Verificación de integridad y esquema posterior a la restauración.
    /// </summary>
    Validating = 4,

    /// <summary>
    /// Reversión automática en curso debido a fallo en la restauración.
    /// </summary>
    Recovering = 5,

    /// <summary>
    /// Fallo crítico durante la restauración o recuperación. Requiere intervención manual.
    /// </summary>
    Failed = 6
}
