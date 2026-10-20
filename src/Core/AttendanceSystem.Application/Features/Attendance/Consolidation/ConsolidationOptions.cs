namespace AttendanceSystem.Application.Features.Attendance.Consolidation;

/// <summary>
/// Parámetros del consumidor de consolidación (sección de configuración <c>Consolidation</c>).
/// </summary>
public sealed class ConsolidationOptions
{
    public const string SectionName = "Consolidation";

    /// <summary>Espera desde la última ampliación para agrupar marcajes de un mismo empleado (F04-07).</summary>
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Antigüedad máxima de una solicitud antes de procesarla aunque sigan llegando marcajes.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Tiempo durante el cual una solicitud reclamada no puede tomarla otro consumidor.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(15);

    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Sondeo de respaldo cuando no llega ningún aviso.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan FinishedRequestRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ProcessedOutboxRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(6);
}
