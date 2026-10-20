using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Services;

public sealed class AdjustmentDiagnosticService : IAdjustmentDiagnosticService
{
    /// <summary>Autor con el que el recálculo creaba ajustes a partir de DailyAttendance (resultado calculado).</summary>
    private const string DerivedAuthor = "Migración F05";

    private readonly IAttendanceAdjustmentRepository _adjustments;
    private readonly IDailyAttendanceRepository _dailies;
    private readonly IAttendanceRepository _records;
    private readonly IShiftRepository _shifts;

    public AdjustmentDiagnosticService(
        IAttendanceAdjustmentRepository adjustments,
        IDailyAttendanceRepository dailies,
        IAttendanceRepository records,
        IShiftRepository shifts)
    {
        _adjustments = adjustments;
        _dailies = dailies;
        _records = records;
        _shifts = shifts;
    }

    public async Task<AdjustmentDiagnosticReport> DiagnoseAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var from = fromDate.ToDateTime(TimeOnly.MinValue);
        var to = toDate.ToDateTime(TimeOnly.MinValue);

        var adjustments = await _adjustments.GetByDateRangeAsync(from, to, cancellationToken: cancellationToken) ?? new List<AttendanceAdjustment>();
        var dailies = await _dailies.GetByDateRangeAsync(from, to, cancellationToken: cancellationToken) ?? new List<DailyAttendance>();
        var shiftIds = ((await _shifts.GetAllAsync(cancellationToken)) ?? Enumerable.Empty<Domain.Aggregates.ShiftAggregate.Shift>()).Select(s => s.Id).ToHashSet();

        var findings = new List<AdjustmentFinding>();
        var adjustmentKeys = adjustments.Select(a => (a.EmployeeId, a.Date.Date)).ToHashSet();
        int preservable = 0;

        foreach (var daily in dailies.Where(d => d.HasManualAdjustments && !adjustmentKeys.Contains((d.EmployeeId, d.Date.Date))))
        {
            findings.Add(new AdjustmentFinding(AdjustmentFindingKind.LostDecision, daily.EmployeeId.Value, DateOnly.FromDateTime(daily.Date),
                "La jornada indica ajustes manuales, pero no hay decisión guardada: el próximo recálculo no podrá reproducirlos."));
        }

        foreach (var adjustment in adjustments.OrderBy(a => a.Date).ThenBy(a => a.EmployeeId.Value, StringComparer.Ordinal))
        {
            var date = DateOnly.FromDateTime(adjustment.Date);
            var employee = adjustment.EmployeeId.Value;
            var issues = new List<AdjustmentFinding>();

            foreach (var position in AttendanceAdjustment.AllPositions.Where(p => adjustment.OverrideKind(p) != PositionOverrideKind.None))
            {
                var recordId = adjustment.OverrideRecord(position);
                if (recordId == null) continue;
                var record = await _records.GetByIdAsync(recordId, cancellationToken);
                var detail = record == null ? "no existe"
                    : record.EmployeeId != adjustment.EmployeeId ? $"pertenece a {record.EmployeeId.Value}"
                    : record.GetWorkDate(record.TimeZoneId) is var wd && (wd < date.AddDays(-PunchAssignmentPolicy.MaxManualDaysBeforeWorkDate) || wd > date.AddDays(PunchAssignmentPolicy.MaxDaysAfterWorkDate))
                        ? $"es del {wd:yyyy-MM-dd}, fuera de la ventana de la jornada"
                        : null;
                if (detail != null)
                {
                    issues.Add(new AdjustmentFinding(AdjustmentFindingKind.InvalidPunchReference, employee, date, $"{position}: el marcaje {recordId} {detail}."));
                }
            }

            if (adjustment.ShiftIdOverride != null && !shiftIds.Contains(adjustment.ShiftIdOverride))
            {
                issues.Add(new AdjustmentFinding(AdjustmentFindingKind.MissingShift, employee, date, $"El turno {adjustment.ShiftIdOverride.Value} ya no existe."));
            }

            var hasDecision = AttendanceAdjustment.AllPositions.Any(p => adjustment.OverrideKind(p) != PositionOverrideKind.None)
                || adjustment.IsRestDayOverride.HasValue || adjustment.ShiftIdOverride != null || adjustment.TemporaryExitStatus.HasValue
                || adjustment.OvertimeAuthorizedOverride.HasValue || adjustment.CalculateOvertimeBeforeEntryOverride.HasValue;
            if (!hasDecision)
            {
                issues.Add(new AdjustmentFinding(AdjustmentFindingKind.NoEvidenceOfIntent, employee, date,
                    "El ajuste no conserva ninguna decisión; si fue una desasignación, no se puede saber si se quería un vacío forzado. Confirme con RH."));
            }

            if (string.Equals(adjustment.CreatedBy, DerivedAuthor, StringComparison.Ordinal))
            {
                issues.Add(new AdjustmentFinding(AdjustmentFindingKind.DerivedFromCalculatedResult, employee, date,
                    "Creado desde un resultado calculado de DailyAttendance (descanso o salida temporal); confirme que fue una decisión de RH."));
            }

            if (issues.Count == 0)
            {
                preservable++;
                findings.Add(new AdjustmentFinding(AdjustmentFindingKind.Preservable, employee, date, adjustment.Describe()));
            }
            else
            {
                findings.AddRange(issues);
            }
        }

        return new AdjustmentDiagnosticReport(adjustments.Count, preservable, findings);
    }
}
