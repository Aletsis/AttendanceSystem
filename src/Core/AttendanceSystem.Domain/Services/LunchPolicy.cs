namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Regla de comida (caso 11):
/// <list type="bullet">
/// <item>Toda ausencia intermedia de más de 15 min es una salida temporal que RH clasifica (comida, con goce, sin
/// goce o error de checada); mientras está pendiente se descuenta.</item>
/// <item>Con la ventana de comida activa, la primera ausencia que se traslapa con la ventana global se clasifica sola
/// como comida (RH puede cambiarla). Apagada, toda ausencia queda pendiente.</item>
/// <item>De la comida sólo se descuenta el excedente sobre <see cref="PaidMinutes"/> (global, o la del turno).</item>
/// <item>Una ausencia de hasta <see cref="DoubleTapMinutes"/> (15 min por defecto) es doble toque.</item>
/// <item>Un marcaje intermedio sin pareja no es salida temporal ni se descuenta: es una incidencia para revisión.</item>
/// </list>
/// </summary>
/// <param name="DoubleTapMinutes">Caso 14: dos checadas a esta distancia o menos son un toque doble (configurable).</param>
public sealed record LunchPolicy(TimeSpan WindowStart, TimeSpan WindowEnd, int PaidMinutes, bool WindowEnabled = true, int DoubleTapMinutes = LunchPolicy.DoubleTapMaxMinutes)
{
    public static readonly TimeSpan DefaultWindowStart = new(12, 30, 0);
    public static readonly TimeSpan DefaultWindowEnd = new(15, 0, 0);
    public const int DefaultPaidMinutes = 30;
    /// <summary>Ventana nocturna por defecto (cruza medianoche): para turnos nocturnos (decisión 4b).</summary>
    public static readonly TimeSpan DefaultNightWindowStart = new(23, 0, 0);
    public static readonly TimeSpan DefaultNightWindowEnd = new(4, 0, 0);
    public const int DoubleTapMaxMinutes = 15;

    public static LunchPolicy Default { get; } = new(DefaultWindowStart, DefaultWindowEnd, DefaultPaidMinutes);
    public static LunchPolicy DefaultNight { get; } = new(DefaultNightWindowStart, DefaultNightWindowEnd, DefaultPaidMinutes);

    /// <summary>Política efectiva de una jornada: los minutos con goce del turno sustituyen a los globales.</summary>
    public LunchPolicy ForShift(int? shiftPaidMinutes) =>
        shiftPaidMinutes is { } minutes ? this with { PaidMinutes = minutes } : this;

    /// <summary>
    /// La ausencia se traslapa con la ventana (caso 11.3): basta con que una parte caiga dentro. Una ventana con inicio
    /// posterior al fin cruza medianoche (p. ej. 23:00–04:00).
    /// </summary>
    public bool Overlaps(DateTime exitCivil, DateTime returnCivil)
    {
        if (!WindowEnabled) return false;
        for (var day = exitCivil.Date.AddDays(-1); day <= returnCivil.Date; day = day.AddDays(1))
        {
            var start = day.Add(WindowStart);
            var end = WindowStart <= WindowEnd ? day.Add(WindowEnd) : day.AddDays(1).Add(WindowEnd);
            if (exitCivil <= end && returnCivil >= start) return true;
        }
        return false;
    }

    /// <summary>Clasifica pares salida/regreso ya ordenados; un elemento sin regreso es un marcaje huérfano.</summary>
    public IReadOnlyList<IntermediateKind> Classify<T>(IReadOnlyList<(T Exit, DateTime ExitCivil, T? Return, DateTime? ReturnCivil)> pairs)
        where T : class
    {
        var kinds = new IntermediateKind[pairs.Count];
        bool lunchTaken = false;
        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (p.Return == null || !p.ReturnCivil.HasValue)
            {
                kinds[i] = IntermediateKind.Orphan;
                continue;
            }

            var minutes = (p.ReturnCivil.Value - p.ExitCivil).TotalMinutes;
            if (minutes <= DoubleTapMinutes) kinds[i] = IntermediateKind.DoubleTap;
            else if (!lunchTaken && Overlaps(p.ExitCivil, p.ReturnCivil.Value))
            {
                // La primera ausencia en la ventana es la comida; las demás son salidas temporales.
                kinds[i] = IntermediateKind.Lunch;
                lunchTaken = true;
            }
            else kinds[i] = IntermediateKind.TemporaryExit;
        }
        return kinds;
    }
}

public enum IntermediateKind
{
    DoubleTap,
    Lunch,
    TemporaryExit,
    Orphan
}
