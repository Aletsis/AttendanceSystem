using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Services;

public class PunchAssignmentDiagnosticService : IPunchAssignmentDiagnosticService
{
    private readonly IDailyAttendanceRepository _dailyRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepository;
    private readonly IPunchAssignmentRepository _assignmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PunchAssignmentDiagnosticService> _logger;

    public PunchAssignmentDiagnosticService(
        IDailyAttendanceRepository dailyRepository,
        IAttendanceRepository attendanceRepository,
        IAttendanceAdjustmentRepository adjustmentRepository,
        IPunchAssignmentRepository assignmentRepository,
        IUnitOfWork unitOfWork,
        ILogger<PunchAssignmentDiagnosticService> logger)
    {
        _dailyRepository = dailyRepository;
        _attendanceRepository = attendanceRepository;
        _adjustmentRepository = adjustmentRepository;
        _assignmentRepository = assignmentRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    private sealed record Reference(
        AttendanceRecordId RecordId,
        EmployeeId EmployeeId,
        DateOnly WorkDate,
        PunchAssignmentRole Role,
        bool IsManual);

    private sealed record Analysis(
        int DailyCount,
        IReadOnlyList<Reference> References,
        IReadOnlyList<PunchAssignment> Backfill,
        int AlreadyMaterialized,
        int ProcessedWithoutAssignment,
        IReadOnlyList<PunchAssignmentIssue> Issues,
        IReadOnlyList<JornadaKey> JornadasToRecalculate);

    public async Task<PunchAssignmentDiagnosticReport> DiagnoseAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(fromDate, toDate, cancellationToken);

        return new PunchAssignmentDiagnosticReport(
            analysis.DailyCount,
            analysis.References.Count,
            analysis.AlreadyMaterialized,
            analysis.Backfill.Count,
            analysis.ProcessedWithoutAssignment,
            analysis.Issues,
            analysis.JornadasToRecalculate);
    }

    public async Task<PunchAssignmentRepairResult> RepairAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(fromDate, toDate, cancellationToken);
        var skipped = analysis.Issues.Select(i => i.RecordId).Distinct().Count();

        if (!dryRun && analysis.Backfill.Count > 0)
        {
            foreach (var assignment in analysis.Backfill)
            {
                _assignmentRepository.Add(assignment);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Reparación de asignaciones {Mode}: {Created} materializadas, {Existing} ya existentes, {Skipped} marcajes para revisión.",
            dryRun ? "simulada" : "aplicada",
            analysis.Backfill.Count,
            analysis.AlreadyMaterialized,
            skipped);

        return new PunchAssignmentRepairResult(
            analysis.Backfill.Count,
            analysis.AlreadyMaterialized,
            skipped,
            dryRun);
    }

    private async Task<Analysis> AnalyzeAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var from = fromDate.ToDateTime(TimeOnly.MinValue);
        var to = toDate.ToDateTime(TimeOnly.MinValue);

        var dailies = (await _dailyRepository.GetByDateRangeAsync(from, to, cancellationToken: cancellationToken)) ?? new List<DailyAttendance>();
        var adjustments = (await _adjustmentRepository.GetByDateRangeAsync(from, to, cancellationToken: cancellationToken)) ?? new List<AttendanceAdjustment>();
        var records = (await _attendanceRepository.GetByDateRangeAsync(
            fromDate.AddDays(-PunchAssignmentPolicy.MaxManualDaysBeforeWorkDate),
            toDate.AddDays(PunchAssignmentPolicy.MaxDaysAfterWorkDate),
            null,
            cancellationToken)) ?? new List<AttendanceRecord>();

        var recordsById = records.ToDictionary(r => r.Id);
        var adjustmentsByJornada = adjustments.ToDictionary(a => (a.EmployeeId, DateOnly.FromDateTime(a.Date)));

        var references = new List<Reference>();
        foreach (var daily in dailies.OrderBy(d => d.Date).ThenBy(d => d.EmployeeId.Value, StringComparer.Ordinal))
        {
            var workDate = DateOnly.FromDateTime(daily.Date);
            adjustmentsByJornada.TryGetValue((daily.EmployeeId, workDate), out var adjustment);

            void Add(AttendanceRecordId? id, PunchAssignmentRole role, AttendanceRecordId? manualId)
            {
                if (id == null) return;
                references.Add(new Reference(id, daily.EmployeeId, workDate, role, manualId != null && manualId == id));
            }

            Add(daily.CheckInRecordId, PunchAssignmentRole.CheckIn, adjustment?.CheckInRecordId);
            Add(daily.Block1CheckOutRecordId, PunchAssignmentRole.Block1CheckOut, adjustment?.Block1CheckOutRecordId);
            Add(daily.Block2CheckInRecordId, PunchAssignmentRole.Block2CheckIn, adjustment?.Block2CheckInRecordId);
            Add(daily.CheckOutRecordId, PunchAssignmentRole.CheckOut, adjustment?.CheckOutRecordId);
        }

        // Marcajes referenciados fuera de la ventana cargada (p. ej. de otro empleado).
        foreach (var missingId in references.Select(r => r.RecordId).Distinct().Where(id => !recordsById.ContainsKey(id)).ToList())
        {
            var record = await _attendanceRepository.GetByIdAsync(missingId, cancellationToken);
            if (record != null) recordsById[record.Id] = record;
        }

        var referencedIds = references.Select(r => r.RecordId).Distinct().ToList();
        var storedRows = ((await _assignmentRepository.GetByRecordIdsAsync(
                referencedIds.Concat(records.Select(r => r.Id)).Distinct().ToList(),
                cancellationToken)) ?? new List<PunchAssignment>())
            .ToDictionary(a => a.AttendanceRecordId);

        var issues = new List<PunchAssignmentIssue>();
        var backfill = new List<PunchAssignment>();
        var toRecalculate = new HashSet<JornadaKey>();
        int alreadyMaterialized = 0;

        foreach (var group in references.GroupBy(r => r.RecordId))
        {
            var refs = group.ToList();
            var recordId = group.Key;
            var workDates = refs.Select(r => r.WorkDate).Distinct().OrderBy(d => d).ToList();
            var involvesManual = refs.Any(r => r.IsManual);
            var flagged = false;

            void Flag(PunchAssignmentIssueKind kind, string employeeId, string detail)
            {
                issues.Add(new PunchAssignmentIssue(kind, recordId.Value.ToString(), employeeId, workDates, involvesManual, detail));
                foreach (var r in refs) toRecalculate.Add(new JornadaKey(r.EmployeeId.Value, r.WorkDate));
                flagged = true;
            }

            if (refs.Count > 1)
            {
                var manualOwner = refs.FirstOrDefault(r => r.IsManual);
                var suggestion = manualOwner != null
                    ? $"Conservar la decisión manual en {manualOwner.WorkDate:yyyy-MM-dd} ({manualOwner.Role}) y recalcular las demás jornadas."
                    : "Recalcular las jornadas en orden cronológico; la más temprana conserva el marcaje.";
                Flag(PunchAssignmentIssueKind.MultipleReferences, refs[0].EmployeeId.Value,
                    $"Referenciado {refs.Count} veces: {string.Join(", ", refs.Select(r => $"{r.WorkDate:yyyy-MM-dd}/{r.Role}"))}. {suggestion}");
            }

            if (!recordsById.TryGetValue(recordId, out var record))
            {
                Flag(PunchAssignmentIssueKind.MissingRecord, refs[0].EmployeeId.Value, "El marcaje referenciado no existe.");
                continue;
            }

            foreach (var reference in refs.Where(r => r.EmployeeId != record.EmployeeId))
            {
                Flag(PunchAssignmentIssueKind.ForeignEmployee, reference.EmployeeId.Value,
                    $"La jornada {reference.WorkDate:yyyy-MM-dd} referencia un marcaje de {record.EmployeeId.Value}.");
            }

            var recordWorkDate = record.GetWorkDate(record.TimeZoneId);
            foreach (var reference in refs.Where(r =>
                         recordWorkDate < r.WorkDate.AddDays(-PunchAssignmentPolicy.MaxManualDaysBeforeWorkDate) ||
                         recordWorkDate > r.WorkDate.AddDays(PunchAssignmentPolicy.MaxDaysAfterWorkDate)))
            {
                Flag(PunchAssignmentIssueKind.TemporalIncompatibility, reference.EmployeeId.Value,
                    $"Marcaje del {recordWorkDate:yyyy-MM-dd} en la jornada {reference.WorkDate:yyyy-MM-dd}.");
            }

            if (record.Status != AttendanceStatus.Processed)
            {
                Flag(PunchAssignmentIssueKind.IncompatibleStatus, record.EmployeeId.Value,
                    $"Marcaje referenciado con estado {record.Status.Name}.");
            }

            if (storedRows.TryGetValue(recordId, out var row))
            {
                var matching = refs.FirstOrDefault(r => r.WorkDate == row.WorkDate && r.EmployeeId == row.EmployeeId);
                if (matching == null)
                {
                    Flag(PunchAssignmentIssueKind.ConflictingAssignmentRow, row.EmployeeId.Value,
                        $"La fila de asignación apunta a {row.WorkDate:yyyy-MM-dd} ({row.Role}).");
                }
                else if (!flagged)
                {
                    alreadyMaterialized++;
                }
                continue;
            }

            if (flagged) continue;

            var single = refs[0];
            backfill.Add(PunchAssignment.Create(
                recordId,
                single.EmployeeId,
                single.WorkDate,
                single.Role,
                single.IsManual ? PunchAssignmentOrigin.Manual : PunchAssignmentOrigin.Automatic));
        }

        var referencedSet = referencedIds.ToHashSet();
        var processedWithoutAssignment = records.Count(r =>
            r.Status == AttendanceStatus.Processed &&
            !referencedSet.Contains(r.Id) &&
            !storedRows.ContainsKey(r.Id));

        return new Analysis(
            dailies.Count,
            references,
            backfill,
            alreadyMaterialized,
            processedWithoutAssignment,
            issues,
            toRecalculate.OrderBy(k => k.WorkDate).ThenBy(k => k.EmployeeId, StringComparer.Ordinal).ToList());
    }
}
