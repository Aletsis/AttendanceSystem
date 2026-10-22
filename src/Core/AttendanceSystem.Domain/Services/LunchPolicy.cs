namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Regla de comida acordada (F06-05):
/// <list type="bullet">
/// <item>La comida no tiene hora fija por turno: es una ausencia marcada cuya salida cae dentro de la ventana global
/// de comida.</item>
/// <item>Con el descuento activado se descuenta la comida completa y RH puede perdonarla; desactivado no se descuenta.</item>
/// <item>Si hay varias ausencias en la ventana, la comida es la de duración más cercana a la de referencia
/// (<see cref="PaidMinutes"/>: global, o la del turno si la define; empate: la primera); las demás son salidas temporales.</item>
/// <item>Una ausencia de hasta 15 min es doble toque; fuera de la ventana, salida temporal.</item>
/// <item>Un marcaje intermedio sin pareja no es salida temporal ni se descuenta: es una incidencia para revisión.</item>
/// </list>
/// </summary>
public sealed record LunchPolicy(TimeSpan WindowStart, TimeSpan WindowEnd, int PaidMinutes, bool DeductLunch = false)
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

    /// <summary>Una ventana con inicio posterior al fin cruza medianoche (p. ej. 23:00–04:00).</summary>
    public bool InWindow(DateTime exitCivil)
    {
        var time = exitCivil.TimeOfDay;
        return WindowStart <= WindowEnd
            ? time >= WindowStart && time <= WindowEnd
            : time >= WindowStart || time <= WindowEnd;
    }

    /// <summary>Clasifica pares salida/regreso ya ordenados; un elemento sin regreso es un marcaje huérfano.</summary>
    public IntermediateClassification Classify<T>(IReadOnlyList<(T Exit, DateTime ExitCivil, T? Return, DateTime? ReturnCivil)> pairs)
        where T : class
    {
        var lunchIndex = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (p.Return == null || !p.ReturnCivil.HasValue) continue;
            var minutes = (p.ReturnCivil.Value - p.ExitCivil).TotalMinutes;
            if (minutes <= DoubleTapMaxMinutes || !InWindow(p.ExitCivil)) continue;
            var distance = Math.Abs(minutes - PaidMinutes);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                lunchIndex = i;
            }
        }

        var kinds = new IntermediateKind[pairs.Count];
        int lunchMinutes = 0, temporaryMinutes = 0;
        bool hasTemporary = false;
        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (p.Return == null || !p.ReturnCivil.HasValue)
            {
                kinds[i] = IntermediateKind.Orphan;
                continue;
            }

            var minutes = (int)(p.ReturnCivil.Value - p.ExitCivil).TotalMinutes;
            if (minutes <= DoubleTapMaxMinutes) kinds[i] = IntermediateKind.DoubleTap;
            else if (i == lunchIndex)
            {
                kinds[i] = IntermediateKind.Lunch;
                lunchMinutes = minutes;
            }
            else
            {
                kinds[i] = IntermediateKind.TemporaryExit;
                hasTemporary = true;
                temporaryMinutes += minutes;
            }
        }

        return new IntermediateClassification(kinds, lunchMinutes, DeductLunch ? lunchMinutes : 0, hasTemporary, temporaryMinutes);
    }
}

public enum IntermediateKind
{
    DoubleTap,
    Lunch,
    TemporaryExit,
    Orphan
}

/// <param name="Kinds">Clasificación de cada par, en el orden recibido.</param>
/// <param name="LunchMinutes">Duración real de la comida (0 si no hubo).</param>
/// <param name="LunchDeductibleMinutes">Minutos de comida a descontar: la comida completa, o 0 si el descuento está desactivado.</param>
public sealed record IntermediateClassification(
    IReadOnlyList<IntermediateKind> Kinds,
    int LunchMinutes,
    int LunchDeductibleMinutes,
    bool HasTemporaryExits,
    int TemporaryExitMinutes);
