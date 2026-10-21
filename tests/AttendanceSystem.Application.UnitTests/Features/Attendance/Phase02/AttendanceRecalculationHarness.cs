using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;

/// <summary>
/// Almacén en memoria con semántica de unidad de trabajo: las altas y bajas quedan preparadas hasta
/// <see cref="IUnitOfWork.SaveChangesAsync"/> y una transacción revertida restaura el estado previo,
/// incluidos los cambios en sitio de marcajes y asignaciones. Permite ejecutar el orquestador y los
/// subcomandos reales sin base de datos (la verificación relacional está en Infrastructure.Tests).
/// </summary>
public sealed class InMemoryAttendanceStore
{
    public List<Employee> Employees { get; } = new();
    public List<Shift> Shifts { get; } = new();
    public List<ShiftRoster> Rosters { get; } = new();
    public List<AttendanceRecord> Records { get; } = new();
    public List<DailyAttendance> Dailies { get; } = new();
    public List<AttendanceAdjustment> Adjustments { get; } = new();
    public List<PunchAssignment> Assignments { get; } = new();
    public List<AttendanceAdjustmentHistory> AdjustmentHistory { get; } = new();
    public List<(EmployeeId EmployeeId, DateTime Start, DateTime End)> ConsolidationRequests { get; } = new();

    internal readonly List<DailyAttendance> PendingDailyAdds = new();
    internal readonly List<DailyAttendance> PendingDailyRemoves = new();
    internal readonly List<PunchAssignment> PendingAssignmentAdds = new();
    internal readonly List<PunchAssignment> PendingAssignmentRemoves = new();
    internal readonly List<AttendanceAdjustment> PendingAdjustmentAdds = new();

    public int SaveChangesCount { get; private set; }

    private Snapshot? _transactionSnapshot;

    public void Save()
    {
        foreach (var removed in PendingDailyRemoves) Dailies.Remove(removed);
        foreach (var added in PendingDailyAdds)
        {
            if (Dailies.Any(d => d.EmployeeId == added.EmployeeId && d.Date.Date == added.Date.Date))
                throw new InvalidOperationException($"Índice único (EmployeeId, Date) violado: {added.EmployeeId.Value} {added.Date:yyyy-MM-dd}");
            Dailies.Add(added);
        }

        foreach (var removed in PendingAssignmentRemoves) Assignments.Remove(removed);
        foreach (var added in PendingAssignmentAdds)
        {
            if (Assignments.Any(a => a.AttendanceRecordId == added.AttendanceRecordId))
                throw new InvalidOperationException($"Clave primaria de asignación violada: {added.AttendanceRecordId}");
            var record = Records.FirstOrDefault(r => r.Id == added.AttendanceRecordId);
            if (record == null || record.EmployeeId != added.EmployeeId)
                throw new InvalidOperationException($"FK de pertenencia violada: {added.AttendanceRecordId}");
            Assignments.Add(added);
        }

        Adjustments.AddRange(PendingAdjustmentAdds);

        PendingDailyAdds.Clear();
        PendingDailyRemoves.Clear();
        PendingAssignmentAdds.Clear();
        PendingAssignmentRemoves.Clear();
        PendingAdjustmentAdds.Clear();
        SaveChangesCount++;
    }

    public void Begin() => _transactionSnapshot = Snapshot.Take(this);

    public void DiscardPending()
    {
        PendingDailyAdds.Clear();
        PendingDailyRemoves.Clear();
        PendingAssignmentAdds.Clear();
        PendingAssignmentRemoves.Clear();
        PendingAdjustmentAdds.Clear();
    }

    public void Commit() => _transactionSnapshot = null;

    public void Rollback()
    {
        PendingDailyAdds.Clear();
        PendingDailyRemoves.Clear();
        PendingAssignmentAdds.Clear();
        PendingAssignmentRemoves.Clear();
        PendingAdjustmentAdds.Clear();
        _transactionSnapshot?.Restore(this);
        _transactionSnapshot = null;
    }

    private sealed class Snapshot
    {
        private readonly List<DailyAttendance> _dailies;
        private readonly List<AttendanceAdjustment> _adjustments;
        private readonly List<(PunchAssignment Row, DateOnly WorkDate, PunchAssignmentRole Role, PunchAssignmentOrigin Origin, DateTime At)> _assignments;
        private readonly List<(AttendanceRecord Record, AttendanceStatus Status, CheckType Type)> _records;

        private Snapshot(InMemoryAttendanceStore store)
        {
            _dailies = store.Dailies.ToList();
            _adjustments = store.Adjustments.ToList();
            _assignments = store.Assignments.Select(a => (a, a.WorkDate, a.Role, a.Origin, a.AssignedAt)).ToList();
            _records = store.Records.Select(r => (r, r.Status, r.CheckType)).ToList();
        }

        public static Snapshot Take(InMemoryAttendanceStore store) => new(store);

        public void Restore(InMemoryAttendanceStore store)
        {
            store.Dailies.Clear();
            store.Dailies.AddRange(_dailies);
            store.Adjustments.Clear();
            store.Adjustments.AddRange(_adjustments);
            store.Assignments.Clear();
            foreach (var (row, workDate, role, origin, at) in _assignments)
            {
                row.Reassign(workDate, role, origin, at);
                store.Assignments.Add(row);
            }
            foreach (var (record, status, type) in _records)
            {
                if (status == AttendanceStatus.Pending) record.ResetStatus(); else record.MarkAsProcessed();
                record.SetInferredType(type);
            }
        }
    }
}

internal sealed class InMemoryDailyAttendanceRepository : IDailyAttendanceRepository
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryDailyAttendanceRepository(InMemoryAttendanceStore store) => _store = store;

    public Task<DailyAttendance?> GetByIdAsync(DailyAttendanceId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Dailies.FirstOrDefault(d => d.Id == id));

    public Task<DailyAttendance?> GetByEmployeeAndDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Dailies.FirstOrDefault(d => d.EmployeeId == employeeId && d.Date.Date == date.Date));

    public Task<List<DailyAttendance>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, BranchId? branchId = null, EmployeeId? employeeId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Dailies
            .Where(d => d.Date.Date >= startDate.Date && d.Date.Date <= endDate.Date)
            .Where(d => employeeId == null || d.EmployeeId == employeeId)
            .Where(d => branchId == null || _store.Employees.Any(e => e.Id == d.EmployeeId && e.BranchId == branchId))
            .ToList());

    public Task<List<DailyAttendance>> GetByEmployeeAndDateRangeAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) =>
        GetByDateRangeAsync(startDate, endDate, null, employeeId, cancellationToken);

    public void Add(DailyAttendance dailyAttendance) => _store.PendingDailyAdds.Add(dailyAttendance);
    public void Update(DailyAttendance dailyAttendance) { }
    public void Remove(DailyAttendance dailyAttendance) => _store.PendingDailyRemoves.Add(dailyAttendance);
}

internal sealed class InMemoryAttendanceRecordRepository : IAttendanceRepository
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryAttendanceRecordRepository(InMemoryAttendanceStore store) => _store = store;

    public Task<AttendanceRecord?> GetByIdAsync(AttendanceRecordId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Records.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<AttendanceRecord>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDate, EmployeeId? employeeId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceRecord>>(_store.Records
            .Where(r => r.WorkDate >= startDate && r.WorkDate <= endDate)
            .Where(r => employeeId == null || r.EmployeeId == employeeId)
            .OrderByDescending(r => r.CheckTime)
            .ToList());

    public Task<IReadOnlyList<AttendanceRecord>> GetByIdentitiesAsync(IReadOnlyCollection<(EmployeeId EmployeeId, DateTime CheckTimeUtc)> identities, CancellationToken cancellationToken = default)
    {
        var keys = identities.Select(i => (i.EmployeeId, i.CheckTimeUtc)).ToHashSet();
        return Task.FromResult<IReadOnlyList<AttendanceRecord>>(_store.Records.Where(r => keys.Contains((r.EmployeeId, r.CheckTime))).ToList());
    }

    public void Detach(AttendanceRecord record) { }

    public Task<IReadOnlyList<AttendanceRecord>> GetByDeviceAndDateRangeAsync(DeviceId deviceId, DateTime startDateTime, DateTime endDateTime, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AttendanceRecord>> GetByDownloadLogIdAsync(DownloadLogId downloadLogId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AttendanceRecord?> GetByEmployeeAndCheckTimeAsync(EmployeeId employeeId, DateTime checkTime, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAsync(AttendanceRecord record, CancellationToken cancellationToken = default) { _store.Records.Add(record); return Task.CompletedTask; }
    public Task AddRangeAsync(IEnumerable<AttendanceRecord> records, CancellationToken cancellationToken = default) { _store.Records.AddRange(records); return Task.CompletedTask; }
    public Task UpdateAsync(AttendanceRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasCheckInForDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AttendanceRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AttendanceRecord>>(_store.Records.ToList());
}

internal sealed class InMemoryAdjustmentRepository : IAttendanceAdjustmentRepository
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryAdjustmentRepository(InMemoryAttendanceStore store) => _store = store;

    public Task<AttendanceAdjustment?> GetByEmployeeAndDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Adjustments.FirstOrDefault(a => a.EmployeeId == employeeId && a.Date.Date == date.Date));

    public Task<IReadOnlyList<AttendanceAdjustment>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, BranchId? branchId = null, EmployeeId? employeeId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceAdjustment>>(_store.Adjustments
            .Where(a => a.Date.Date >= startDate.Date && a.Date.Date <= endDate.Date)
            .Where(a => employeeId == null || a.EmployeeId == employeeId)
            .ToList());

    public Task AddAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken = default) { _store.PendingAdjustmentAdds.Add(adjustment); return Task.CompletedTask; }
    public void Update(AttendanceAdjustment adjustment) { }
    public void Remove(AttendanceAdjustment adjustment) => _store.Adjustments.Remove(adjustment);

    public Task AddHistoryAsync(AttendanceAdjustmentHistory entry, CancellationToken cancellationToken = default) { _store.AdjustmentHistory.Add(entry); return Task.CompletedTask; }

    public Task<IReadOnlyList<AttendanceAdjustmentHistory>> GetHistoryAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceAdjustmentHistory>>(_store.AdjustmentHistory.Where(h => h.EmployeeId == employeeId && h.Date == date.Date).OrderBy(h => h.Revision).ToList());
}

/// <summary>Sólo registra las solicitudes durables; el consumidor se prueba sobre PostgreSQL.</summary>
internal sealed class InMemoryConsolidationRepository : IAttendanceConsolidationRepository
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryConsolidationRepository(InMemoryAttendanceStore store) => _store = store;

    public Task RequestAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        _store.ConsolidationRequests.Add((employeeId, startDate.Date, endDate.Date));
        return Task.CompletedTask;
    }

    public async Task RequestManyAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        foreach (var id in employeeIds) await RequestAsync(id, startDate, endDate, cancellationToken);
    }

    public Task<IReadOnlyList<ConsolidationClaim>> ClaimAsync(string consumerId, int maxBatchSize, TimeSpan lease, TimeSpan quietPeriod, TimeSpan maxDelay, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ConsolidationCompletion> CompleteAsync(Guid requestId, string consumerId, int claimedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task FailAsync(Guid requestId, string consumerId, string error, int maxAttempts, TimeSpan baseBackoff, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReleaseAsync(Guid requestId, string consumerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> RetryFailedAsync(Guid? requestId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AttendanceConsolidationRequest?> GetActiveByEmployeeAsync(EmployeeId employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ConsolidationHealth> GetHealthAsync(int recentFailures = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> PurgeFinishedAsync(TimeSpan retention, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary><see cref="ISender"/> del servicio de decisiones: el recálculo inmediato usa el orquestador real.</summary>
internal sealed class RecalculationSender : ISender
{
    private readonly AttendanceRecalculationHarness _harness;
    public RecalculationSender(AttendanceRecalculationHarness harness) => _harness = harness;

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is not ProcessDailyAttendanceCommand command) throw new NotSupportedException(request.GetType().Name);
        if (_harness.FailNextRecalculation) { _harness.FailNextRecalculation = false; throw new InvalidOperationException("recálculo no disponible"); }
        return (TResponse)(object)await _harness.RecalculateAsync(command.StartDate, command.EndDate, command.EmployeeId);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class InMemoryPunchAssignmentRepository : IPunchAssignmentRepository
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryPunchAssignmentRepository(InMemoryAttendanceStore store) => _store = store;

    public Task<IReadOnlyList<PunchAssignment>> GetByRecordIdsAsync(IReadOnlyCollection<AttendanceRecordId> recordIds, CancellationToken cancellationToken = default)
    {
        var ids = recordIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<PunchAssignment>>(_store.Assignments.Where(a => ids.Contains(a.AttendanceRecordId)).ToList());
    }

    public Task<IReadOnlyList<PunchAssignment>> GetByEmployeeAndWorkDateRangeAsync(EmployeeId employeeId, DateOnly startWorkDate, DateOnly endWorkDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PunchAssignment>>(_store.Assignments
            .Where(a => a.EmployeeId == employeeId && a.WorkDate >= startWorkDate && a.WorkDate <= endWorkDate)
            .ToList());

    public Task<PunchAssignment?> GetByRecordIdAsync(AttendanceRecordId recordId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Assignments.FirstOrDefault(a => a.AttendanceRecordId == recordId));

    public void Add(PunchAssignment assignment) => _store.PendingAssignmentAdds.Add(assignment);
    public void Update(PunchAssignment assignment) { }
    public void Remove(PunchAssignment assignment) => _store.PendingAssignmentRemoves.Add(assignment);
}

internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly InMemoryAttendanceStore _store;
    public InMemoryUnitOfWork(InMemoryAttendanceStore store) => _store = store;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { _store.Save(); return Task.FromResult(1); }
    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default) { _store.Begin(); return Task.FromResult<IAsyncDisposable>(new NoopDisposable()); }
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) { _store.Commit(); return Task.CompletedTask; }
    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) { _store.Rollback(); return Task.CompletedTask; }
    public void DiscardChanges() => _store.DiscardPending();

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// <see cref="ISender"/> mínimo que enruta los subcomandos de cálculo a sus manejadores reales.
/// </summary>
internal sealed class SubCommandRoutingSender : ISender
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;

    public SubCommandRoutingSender(IDailyAttendanceRepository dailyRepo, IAttendanceRepository attendanceRepo)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
        request switch
        {
            ProcessRegularAttendanceCommand c => new ProcessRegularAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessRegularAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
            ProcessNightlyAttendanceCommand c => new ProcessNightlyAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessNightlyAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
            ProcessFlexibleAttendanceCommand c => new ProcessFlexibleAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessFlexibleAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
            ProcessSplitAttendanceCommand c => new ProcessSplitAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessSplitAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
            ProcessNoShiftAttendanceCommand c => new ProcessNoShiftAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessNoShiftAttendanceCommandHandler>.Instance).Handle(c, cancellationToken),
            ProcessContinuousAttendanceCommand c => new ProcessContinuousAttendanceCommandHandler(this).Handle(c, cancellationToken),
            _ => throw new NotSupportedException(request.GetType().Name)
        };

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException(request.GetType().Name);
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException(request.GetType().Name);
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Ejecuta <see cref="ProcessDailyAttendanceCommandHandler"/> real sobre un <see cref="InMemoryAttendanceStore"/>.
/// </summary>
public sealed class AttendanceRecalculationHarness
{
    public InMemoryAttendanceStore Store { get; } = new();

    private readonly InMemoryDailyAttendanceRepository _dailyRepo;
    private readonly InMemoryAttendanceRecordRepository _attendanceRepo;
    private readonly InMemoryAdjustmentRepository _adjustmentRepo;
    private readonly InMemoryPunchAssignmentRepository _assignmentRepo;
    private readonly InMemoryUnitOfWork _unitOfWork;

    public AttendanceRecalculationHarness()
    {
        _dailyRepo = new InMemoryDailyAttendanceRepository(Store);
        _attendanceRepo = new InMemoryAttendanceRecordRepository(Store);
        _adjustmentRepo = new InMemoryAdjustmentRepository(Store);
        _assignmentRepo = new InMemoryPunchAssignmentRepository(Store);
        _unitOfWork = new InMemoryUnitOfWork(Store);
    }

    public IPunchAssignmentRepository AssignmentRepository => _assignmentRepo;
    public IDailyAttendanceRepository DailyRepository => _dailyRepo;
    public IAttendanceRepository AttendanceRepository => _attendanceRepo;
    public IAttendanceAdjustmentRepository AdjustmentRepository => _adjustmentRepo;
    public IUnitOfWork UnitOfWork => _unitOfWork;

    /// <summary>Simula que el recálculo inmediato tras una decisión falla (la decisión queda pendiente).</summary>
    public bool FailNextRecalculation { get; set; }

    public string CurrentUser { get; set; } = "rh.operador";

    /// <summary>Configuración del sistema que ve el orquestador (período contable del tope de extras).</summary>
    public SystemConfiguration? Configuration { get; set; }

    /// <summary>Servicio de decisiones con los repositorios del almacén y el orquestador real.</summary>
    public AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService Decisions()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.UserName).Returns(() => CurrentUser);
        return new AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService(
            _adjustmentRepo,
            new InMemoryConsolidationRepository(Store),
            NoOpConsolidationWakeup.Instance,
            _unitOfWork,
            LockService,
            new RecalculationSender(this),
            NullLogger<AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService>.Instance,
            user.Object);
    }

    public AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance.ManuallyAssignAttendanceCommandHandler ManualAssign() =>
        new(_dailyRepo, _attendanceRepo, _assignmentRepo, Decisions());

    /// <summary>Sin base compartida no hay otros procesos: el bloqueo es una operación vacía.</summary>
    public IAttendanceCalculationLockService LockService { get; } = Mock.Of<IAttendanceCalculationLockService>();

    public Task<int> RecalculateAsync(DateTime start, DateTime end, EmployeeId? employeeId = null)
    {
        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Store.Employees.ToList());

        var shiftRepo = new Mock<IShiftRepository>();
        shiftRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Store.Shifts.ToList());

        var rosterRepo = new Mock<IShiftRosterRepository>();
        rosterRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime s, DateTime e, BranchId? _, EmployeeId? emp, CancellationToken _) =>
                Store.Rosters.Where(r => r.Date.Date >= s.Date && r.Date.Date <= e.Date && (emp == null || r.EmployeeId == emp)).ToList());

        var configRepo = new Mock<ISystemConfigurationRepository>();
        configRepo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Configuration);

        var consolidationRepo = new Mock<IAttendanceConsolidationRepository>();


        var handler = new ProcessDailyAttendanceCommandHandler(
            _dailyRepo,
            _attendanceRepo,
            employeeRepo.Object,
            shiftRepo.Object,
            rosterRepo.Object,
            configRepo.Object,
            _adjustmentRepo,
            _assignmentRepo,
            LockService,
            _unitOfWork,
            new SubCommandRoutingSender(_dailyRepo, _attendanceRepo),
            NullLogger<ProcessDailyAttendanceCommandHandler>.Instance);

        return handler.Handle(new ProcessDailyAttendanceCommand(start, end, EmployeeId: employeeId), CancellationToken.None);
    }

    public DailyAttendance? Daily(EmployeeId employeeId, DateTime date) =>
        Store.Dailies.SingleOrDefault(d => d.EmployeeId == employeeId && d.Date.Date == date.Date);

    public AttendanceRecord Punch(EmployeeId employeeId, DateTime civil) =>
        Store.Records.Single(r => r.EmployeeId == employeeId && r.OriginalCheckTime == civil);
}

/// <summary>
/// Comparador de resultados (entregable F02): proyección canónica e independiente de identificadores
/// generados, para comparar recálculo completo, repetido y particionado.
/// </summary>
public static class AttendanceResultSnapshot
{
    public static IReadOnlyList<string> Capture(InMemoryAttendanceStore store)
    {
        string Punch(AttendanceRecordId? id)
        {
            if (id == null) return "-";
            var record = store.Records.Single(r => r.Id == id);
            return $"{record.OriginalCheckTime:yyyy-MM-dd HH:mm}";
        }

        var lines = new List<string>();

        foreach (var d in store.Dailies.OrderBy(d => d.EmployeeId.Value, StringComparer.Ordinal).ThenBy(d => d.Date))
        {
            lines.Add(string.Join("|",
                "DA", d.EmployeeId.Value, d.Date.ToString("yyyy-MM-dd"), d.ShiftName ?? "-",
                $"in={Punch(d.CheckInRecordId)}", $"b1={Punch(d.Block1CheckOutRecordId)}",
                $"b2={Punch(d.Block2CheckInRecordId)}", $"out={Punch(d.CheckOutRecordId)}",
                $"late={d.LateMinutes}", $"early={d.EarlyDepartureMinutes}", $"worked={d.TotalWorkedMinutes}",
                $"ot={d.OvertimeMinutes}", $"absent={d.IsAbsent}", $"rest={d.IsRestDay}",
                $"missIn={d.MissingCheckIn}", $"missOut={d.MissingCheckOut}",
                $"lunch={d.LunchBreakMinutesApplied}", $"temp={d.HasTemporaryExits}/{d.TemporaryExitMinutes}",
                $"intervals={d.IntervalsData ?? "-"}"));
        }

        foreach (var a in store.Assignments.OrderBy(a => a.EmployeeId.Value, StringComparer.Ordinal).ThenBy(a => Punch(a.AttendanceRecordId), StringComparer.Ordinal))
        {
            lines.Add(string.Join("|", "PA", a.EmployeeId.Value, Punch(a.AttendanceRecordId), a.WorkDate.ToString("yyyy-MM-dd"), a.Role, a.Origin));
        }

        foreach (var r in store.Records.OrderBy(r => r.EmployeeId.Value, StringComparer.Ordinal).ThenBy(r => r.CheckTime))
        {
            lines.Add(string.Join("|", "AR", r.EmployeeId.Value, $"{r.OriginalCheckTime:yyyy-MM-dd HH:mm}", r.Status.Name, r.CheckType.Name));
        }

        return lines;
    }

    /// <summary>Diferencias legibles entre dos proyecciones (vacío si son idénticas).</summary>
    public static IReadOnlyList<string> Diff(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        var missing = expected.Except(actual).Select(l => "- " + l);
        var extra = actual.Except(expected).Select(l => "+ " + l);
        return missing.Concat(extra).ToList();
    }
}
