using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// F07-01: métricas y trazas operativas del cálculo de asistencia (System.Diagnostics, exportables con
/// OpenTelemetry o dotnet-counters). Las etiquetas nunca llevan datos personales: sin identificadores de empleado
/// ni nombres; la correlación se hace con el identificador de traza/actividad.
/// </summary>
/// <summary>Salud operativa de consolidación y outbox (F07-01).</summary>
public sealed record OperationalHealth(int PendingRequests, int FailedRequests, double OldestPendingAgeSeconds, int PendingOutbox, int DeadLetters)
{
    public static readonly OperationalHealth Unknown = new(0, 0, 0, 0, 0);
}

public static class AttendanceTelemetry
{
    public const string Name = "AttendanceSystem.Attendance";

    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);

    /// <summary>Duración de una unidad de recálculo (empleado + ventana de dependencias), en ms.</summary>
    public static readonly Histogram<double> UnitDuration =
        Meter.CreateHistogram<double>("attendance.recalc.unit.duration", "ms", "Duración de la unidad de recálculo");

    /// <summary>Duración por empleado-día de la unidad (duración ÷ jornadas reconstruidas), en ms.</summary>
    public static readonly Histogram<double> EmployeeDayDuration =
        Meter.CreateHistogram<double>("attendance.recalc.employee_day.duration", "ms", "Duración por empleado-día");

    /// <summary>Duración por etapa del lote (load, compute, save, overtime, commit), en ms.</summary>
    public static readonly Histogram<double> StageDuration =
        Meter.CreateHistogram<double>("attendance.recalc.stage.duration", "ms", "Duración por etapa del lote de recálculo");

    public static void RecordStage(string stage, System.Diagnostics.Stopwatch stopwatch)
    {
        StageDuration.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("stage", stage));
        stopwatch.Restart();
    }

    public static readonly Histogram<int> UnitDays =
        Meter.CreateHistogram<int>("attendance.recalc.unit.days", "{day}", "Jornadas reconstruidas por unidad");

    public static readonly Histogram<int> UnitPunches =
        Meter.CreateHistogram<int>("attendance.recalc.unit.punches", "{punch}", "Marcajes cargados por unidad");

    public static readonly Counter<long> Units =
        Meter.CreateCounter<long>("attendance.recalc.units", "{unit}", "Unidades de recálculo por resultado");

    public static readonly Counter<long> ConsolidationClaims =
        Meter.CreateCounter<long>("attendance.consolidation.claims", "{request}", "Solicitudes de consolidación atendidas por resultado");

    public static readonly Counter<long> DeviceClockCorrections =
        Meter.CreateCounter<long>("attendance.device.clock_corrections", "{correction}", "Relojes resincronizados por desvío de hora");

    /// <summary>Última lectura de salud (la actualiza el consumidor de consolidación).</summary>
    public static OperationalHealth LastHealth { get; set; } = OperationalHealth.Unknown;

    static AttendanceTelemetry()
    {
        Meter.CreateObservableGauge("attendance.consolidation.pending", () => LastHealth.PendingRequests, "{request}", "Solicitudes pendientes");
        Meter.CreateObservableGauge("attendance.consolidation.failed", () => LastHealth.FailedRequests, "{request}", "Solicitudes con reintentos agotados");
        Meter.CreateObservableGauge("attendance.consolidation.oldest_pending_age", () => LastHealth.OldestPendingAgeSeconds, "s", "Antigüedad de la solicitud pendiente más antigua");
        Meter.CreateObservableGauge("attendance.outbox.pending", () => LastHealth.PendingOutbox, "{message}", "Eventos pendientes de publicar");
        Meter.CreateObservableGauge("attendance.outbox.dead_letters", () => LastHealth.DeadLetters, "{message}", "Eventos en dead letter");
    }

    /// <summary>Espera para adquirir el bloqueo por empleado, en ms.</summary>
    public static readonly Histogram<double> LockWait =
        Meter.CreateHistogram<double>("attendance.lock.wait", "ms", "Espera del bloqueo de asistencia");
}
