using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

/// <summary>
/// Resultado en memoria del cálculo de una jornada (F02-06): la asistencia diaria producida y los
/// marcajes que consumió sin ocupar una posición principal. El orquestador lo valida y lo
/// convierte en <see cref="PunchAssignment"/> antes de persistir.
/// </summary>
public sealed class PunchAssignmentDraft
{
    private readonly Dictionary<AttendanceRecordId, PunchAssignmentRole> _secondary = new();

    public DailyAttendance? DailyAttendance { get; private set; }

    public IReadOnlyDictionary<AttendanceRecordId, PunchAssignmentRole> Secondary => _secondary;

    public void Intermediate(AttendanceRecord record) => _secondary[record.Id] = PunchAssignmentRole.Intermediate;

    public void IntervalBoundary(AttendanceRecord record) => _secondary[record.Id] = PunchAssignmentRole.IntervalBoundary;

    public void DoubleTap(AttendanceRecord record) => _secondary[record.Id] = PunchAssignmentRole.DiscardedDoubleTap;

    public void PendingReview(AttendanceRecord record) => _secondary[record.Id] = PunchAssignmentRole.PendingReview;

    public void Complete(DailyAttendance dailyAttendance) => DailyAttendance = dailyAttendance;

    /// <summary>
    /// Asignaciones resultantes. Las posiciones se toman del estado final de la jornada (incluidos
    /// ajustes manuales); un marcaje en posición deja de figurar como secundario.
    /// </summary>
    public List<PunchAssignment> BuildAssignments(AttendanceAdjustment? adjustment, DateTime assignedAt)
    {
        var result = new List<PunchAssignment>();
        var daily = DailyAttendance;
        if (daily == null) return result;

        var workDate = DateOnly.FromDateTime(daily.Date);

        void AddPosition(AttendanceRecordId? recordId, PunchAssignmentRole role, AttendanceRecordId? manualRecordId)
        {
            if (recordId == null) return;
            var origin = manualRecordId != null && manualRecordId == recordId
                ? PunchAssignmentOrigin.Manual
                : PunchAssignmentOrigin.Automatic;
            result.Add(PunchAssignment.Create(recordId, daily.EmployeeId, workDate, role, origin, assignedAt));
        }

        AttendanceRecordId? Manual(AttendancePosition position) =>
            adjustment?.OverrideKind(position) == PositionOverrideKind.Assigned ? adjustment.OverrideRecord(position) : null;

        AddPosition(daily.CheckInRecordId, PunchAssignmentRole.CheckIn, Manual(AttendancePosition.CheckIn));
        AddPosition(daily.Block1CheckOutRecordId, PunchAssignmentRole.Block1CheckOut, Manual(AttendancePosition.Block1CheckOut));
        AddPosition(daily.Block2CheckInRecordId, PunchAssignmentRole.Block2CheckIn, Manual(AttendancePosition.Block2CheckIn));
        AddPosition(daily.CheckOutRecordId, PunchAssignmentRole.CheckOut, Manual(AttendancePosition.CheckOut));

        var positioned = result.Select(a => a.AttendanceRecordId).ToHashSet();

        // F05-01: un marcaje retirado con vacío forzado queda reservado por la jornada como excluido.
        foreach (var excluded in adjustment?.ExcludedRecordIds() ?? Enumerable.Empty<AttendanceRecordId>())
        {
            if (!positioned.Add(excluded)) continue;
            result.Add(PunchAssignment.Create(excluded, daily.EmployeeId, workDate, PunchAssignmentRole.ManuallyExcluded, PunchAssignmentOrigin.Manual, assignedAt));
        }

        foreach (var (recordId, role) in _secondary.OrderBy(kv => kv.Key.Value))
        {
            if (positioned.Contains(recordId)) continue;
            result.Add(PunchAssignment.Create(recordId, daily.EmployeeId, workDate, role, PunchAssignmentOrigin.Automatic, assignedAt));
        }

        return result;
    }
}
