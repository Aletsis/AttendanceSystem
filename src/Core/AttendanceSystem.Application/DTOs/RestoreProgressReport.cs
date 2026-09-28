namespace AttendanceSystem.Application.DTOs;

public enum RestoreStage
{
    NotStarted = 0,
    SafetySnapshot = 1,       // [1/4] Creación del snapshot de seguridad previo
    PreparingDatabase = 2,    // [2/4] Desconexión de sesiones y limpieza de esquemas
    ExecutingPgRestore = 3,   // [3/4] Ejecución activa de pg_restore
    Finalizing = 4,           // [4/4] Limpieza, reanudación de Hangfire y verificación
    Completed = 5,            // Éxito total
    Failed = 6                // Error crítico (con o sin rollback)
}

public record RestoreProgressReport(
    RestoreStage Stage,
    string StageTitle,
    string? LogLine = null,
    bool IsError = false,
    DateTime? Timestamp = null,
    int? ProgressPercentage = null
)
{
    public DateTime EventTime { get; init; } = Timestamp ?? DateTime.Now;
}
