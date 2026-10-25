namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Caso 18: umbrales de la cuarentena de marcajes (configurables). Una regla manda el marcaje a cuarentena para que el
/// Administrador lo libere con la hora corregida o lo descarte; ninguna lo corrige sola.
/// </summary>
/// <param name="MinDateDaysBeforeDevice">R1: fecha mínima = alta del reloj menos estos días (o la que fije el Administrador).</param>
/// <param name="FutureToleranceMinutes">R2: adelanto tolerado respecto a la hora real de la zona del reloj.</param>
/// <param name="BackwardJumpMinutes">R3: retroceso de la hora, respecto al marcaje anterior del reloj, que indica un salto.</param>
/// <param name="RealtimeLagMinutes">R4: en tiempo real (ADMS), un marcaje de hace más que esto, con el reloj conectado desde antes, es de un reloj atrasado.</param>
/// <param name="ClockDriftAlertMinutes">R5: desfase en tiempo real que sólo genera alerta y resincroniza el reloj.</param>
/// <param name="SdkReadMarginDays">Margen extra al leer un reloj por SDK que estaba atrasado, para no perder marcajes corridos.</param>
public sealed record PunchQuarantinePolicy(
    int MinDateDaysBeforeDevice = 30,
    int FutureToleranceMinutes = 5,
    int BackwardJumpMinutes = 60,
    int RealtimeLagMinutes = 30,
    int ClockDriftAlertMinutes = 10,
    int SdkReadMarginDays = 1)
{
    public static PunchQuarantinePolicy Default { get; } = new();

    public string? Validate() =>
        MinDateDaysBeforeDevice is < 0 or > 3650 ? "La fecha mínima debe estar entre 0 y 3650 días antes del alta del reloj"
        : FutureToleranceMinutes is < 1 or > 1440 ? "La tolerancia hacia el futuro debe estar entre 1 y 1440 minutos"
        : BackwardJumpMinutes is < 5 or > 10080 ? "El salto hacia atrás debe estar entre 5 y 10080 minutos"
        : RealtimeLagMinutes is < 5 or > 10080 ? "El atraso en tiempo real debe estar entre 5 y 10080 minutos"
        : ClockDriftAlertMinutes < 1 || ClockDriftAlertMinutes > RealtimeLagMinutes ? "La alerta de desfase debe estar entre 1 minuto y el atraso en tiempo real"
        : SdkReadMarginDays is < 0 or > 30 ? "El margen de lectura debe estar entre 0 y 30 días"
        : null;
}
