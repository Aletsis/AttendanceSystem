using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

/// <summary>
/// Análisis común de marcajes intermedios con la regla de comida de <see cref="LunchPolicy"/> (caso 11): cada
/// ausencia es una salida (la comida automática si la ventana está activa), los dobles toques se ignoran y un marcaje
/// sin pareja es una incidencia. Registra en el borrador el papel de cada marcaje.
/// </summary>
internal static class IntermediatePunchAnalysis
{
    public static (IReadOnlyList<TemporaryExit> Exits, IReadOnlyList<DateTime> Orphans) Analyze(
        IReadOnlyList<AttendanceRecord> recordsToAnalyze,
        LunchPolicy policy,
        string? timeZoneId,
        PunchAssignmentDraft? draft)
    {
        var pairs = new List<(AttendanceRecord Exit, DateTime ExitCivil, AttendanceRecord? Return, DateTime? ReturnCivil)>();
        for (int i = 0; i < recordsToAnalyze.Count; i += 2)
        {
            var exit = recordsToAnalyze[i];
            var back = i + 1 < recordsToAnalyze.Count ? recordsToAnalyze[i + 1] : null;
            pairs.Add((exit, exit.GetCivilTime(timeZoneId), back, back?.GetCivilTime(timeZoneId)));
        }

        var kinds = policy.Classify(pairs);
        var exits = new List<TemporaryExit>();
        var orphans = new List<DateTime>();
        for (int i = 0; i < pairs.Count; i++)
        {
            var (exit, exitCivil, back, backCivil) = pairs[i];
            switch (kinds[i])
            {
                case IntermediateKind.DoubleTap:
                    draft?.DoubleTap(exit);
                    draft?.DoubleTap(back!);
                    break;
                case IntermediateKind.Orphan:
                    draft?.PendingReview(exit);
                    orphans.Add(exitCivil);
                    break;
                default:
                    draft?.Intermediate(exit);
                    draft?.Intermediate(back!);
                    var isLunch = kinds[i] == IntermediateKind.Lunch;
                    exits.Add(new TemporaryExit(exit.Id.Value, exitCivil, backCivil!.Value, (int)(backCivil.Value - exitCivil).TotalMinutes,
                        isLunch ? TemporaryExitStatus.Lunch : TemporaryExitStatus.Pending, isLunch));
                    break;
            }
        }

        return (exits, orphans);
    }

    /// <summary>Nota para RH de un marcaje intermedio sin pareja (caso 11.e).</summary>
    public static string OrphanIssue(DateTime orphan) =>
        $"Marcaje intermedio sin pareja a las {orphan:HH:mm}: clasifíquelo como error de checada o agregue la checada faltante.";
}
