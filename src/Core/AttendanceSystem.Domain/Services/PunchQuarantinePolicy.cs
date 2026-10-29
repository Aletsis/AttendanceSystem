namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Caso 18: umbrales de la hora de los relojes y de la cuarentena de marcajes (configurables). Una regla manda el
/// marcaje a cuarentena para que el Administrador lo libere con la hora corregida o lo descarte; ninguna lo corrige sola.
/// El desfase de un reloj, en tiempo real o al descargar, se trata igual: hasta <see cref="ClockSyncToleranceMinutes"/>
/// no se hace nada; más, se resincroniza; más de <see cref="ClockDriftAlertMinutes"/>, además queda una alerta; más de
/// <see cref="RealtimeLagMinutes"/>, sus marcajes van a cuarentena.
/// </summary>
/// <param name="MinDateDaysBeforeDevice">R1: fecha mínima = alta del reloj menos estos días (o la que fije el Administrador).</param>
/// <param name="FutureToleranceMinutes">R2: adelanto tolerado respecto a la hora real de la zona del reloj.</param>
/// <param name="BackwardJumpMinutes">R3: retroceso de la hora, respecto al marcaje anterior del reloj, que indica un salto.</param>
/// <param name="RealtimeLagMinutes">Desfase del reloj que manda sus marcajes a cuarentena: R4 en tiempo real (ADMS) y al descargar (SDK).</param>
/// <param name="ClockDriftAlertMinutes">R5: desfase que, sin llegar a la cuarentena, genera alerta (los marcajes entran).</param>
/// <param name="SdkReadMarginDays">Margen extra al leer un reloj por SDK que estaba atrasado, para no perder marcajes corridos.</param>
/// <param name="ClockSyncToleranceMinutes">Desfase tolerado antes de resincronizar el reloj.</param>
public sealed record PunchQuarantinePolicy(
    int MinDateDaysBeforeDevice = 30,
    int FutureToleranceMinutes = 5,
    int BackwardJumpMinutes = 60,
    int RealtimeLagMinutes = 30,
    int ClockDriftAlertMinutes = 10,
    int SdkReadMarginDays = 1,
    int ClockSyncToleranceMinutes = 2)
{
    public static PunchQuarantinePolicy Default { get; } = new();

    public TimeSpan FutureTolerance => TimeSpan.FromMinutes(FutureToleranceMinutes);
    public TimeSpan ClockSyncTolerance => TimeSpan.FromMinutes(ClockSyncToleranceMinutes);
    public TimeSpan ClockDriftAlert => TimeSpan.FromMinutes(ClockDriftAlertMinutes);
    public TimeSpan ClockDriftQuarantine => TimeSpan.FromMinutes(RealtimeLagMinutes);

    public string? Validate() =>
        MinDateDaysBeforeDevice is < 0 or > 3650 ? "La fecha mínima debe estar entre 0 y 3650 días antes del alta del reloj"
        : FutureToleranceMinutes is < 1 or > 1440 ? "La tolerancia hacia el futuro debe estar entre 1 y 1440 minutos"
        : BackwardJumpMinutes is < 5 or > 10080 ? "El salto hacia atrás debe estar entre 5 y 10080 minutos"
        : RealtimeLagMinutes is < 5 or > 10080 ? "El desfase que manda a cuarentena debe estar entre 5 y 10080 minutos"
        : ClockDriftAlertMinutes < 1 || ClockDriftAlertMinutes > RealtimeLagMinutes ? "La alerta de desfase debe estar entre 1 minuto y el desfase que manda a cuarentena"
        : SdkReadMarginDays is < 0 or > 30 ? "El margen de lectura debe estar entre 0 y 30 días"
        : ClockSyncToleranceMinutes < 1 || ClockSyncToleranceMinutes > Math.Min(ClockDriftAlertMinutes, FutureToleranceMinutes)
            ? "La tolerancia de sincronización debe estar entre 1 minuto y la menor entre la alerta de desfase y la tolerancia hacia el futuro"
        : null;
}
