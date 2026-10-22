using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

/// <summary>
/// Análisis común de marcajes intermedios de jornadas de un intervalo (regular y flexible) con la regla de comida
/// de <see cref="LunchPolicy"/> (F06-05). Registra en el borrador el papel de cada marcaje.
/// </summary>
internal static class IntermediatePunchAnalysis
{
    public static (int LunchMinutes, int LunchExcess, bool HasTempExits, int TempMinutes, IReadOnlyList<DateTime> Orphans) Analyze(
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

        var result = policy.Classify(pairs);
        var orphans = new List<DateTime>();
        for (int i = 0; i < pairs.Count; i++)
        {
            var (exit, _, back, _) = pairs[i];
            switch (result.Kinds[i])
            {
                case IntermediateKind.DoubleTap:
                    draft?.DoubleTap(exit);
                    draft?.DoubleTap(back!);
                    break;
                case IntermediateKind.Orphan:
                    draft?.PendingReview(exit);
                    orphans.Add(pairs[i].ExitCivil);
                    break;
                default:
                    draft?.Intermediate(exit);
                    draft?.Intermediate(back!);
                    break;
            }
        }

        return (result.LunchMinutes, result.LunchDeductibleMinutes, result.HasTemporaryExits, result.TemporaryExitMinutes, orphans);
    }
}
