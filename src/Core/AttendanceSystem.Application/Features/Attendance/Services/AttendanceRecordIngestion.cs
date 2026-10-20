using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceSystem.Application.Features.Attendance.Services;

/// <summary>
/// Resultado fiable de una ingesta (F03-05 / F03-06).
/// <see cref="ConfirmedDuplicates"/> sólo incluye marcajes cuya identidad existente se leyó de la base de datos.
/// Si <see cref="Failure"/> no es nulo, ninguno de los pendientes se guardó y la ingesta puede reintentarse.
/// </summary>
public sealed record AttendanceIngestionResult(
    IReadOnlyList<AttendanceRecord> Inserted,
    IReadOnlyDictionary<(EmployeeId EmployeeId, DateTime CheckTimeUtc), AttendanceRecordId> ConfirmedDuplicates,
    int InBatchDuplicates,
    PersistenceFailure? Failure)
{
    public bool IsSuccess => Failure == null;

    /// <summary>Identificador persistido para una identidad (insertado o existente).</summary>
    public AttendanceRecordId? IdFor(EmployeeId employeeId, DateTime checkTimeUtc)
    {
        var inserted = Inserted.FirstOrDefault(r => r.EmployeeId == employeeId && r.CheckTime == checkTimeUtc);
        if (inserted != null) return inserted.Id;
        return ConfirmedDuplicates.TryGetValue((employeeId, checkTimeUtc), out var existing) ? existing : null;
    }
}

/// <summary>
/// Inserción idempotente de marcajes con la identidad única (EmployeeId, CheckTime UTC) (F03-01 / F03-05).
/// <list type="number">
/// <item>Deduplica el lote y consulta las identidades ya existentes, sin importar dispositivo o fuente.</item>
/// <item>Guarda el resto; los eventos de dominio de cada registro insertado viajan al outbox en la misma transacción.</item>
/// <item>Ante una violación de unicidad (otro proceso ganó la carrera) deja de rastrear lo pendiente, vuelve a
/// consultar, separa las colisiones confirmadas y reintenta con un contexto coherente.</item>
/// <item>Cualquier otro fallo (conexión, tiempo, datos) se devuelve como fallo: nunca se cuenta como duplicado.</item>
/// </list>
/// </summary>
public sealed class AttendanceRecordIngestion
{
    private const int MaxUniqueConflictRetries = 3;

    private readonly IAttendanceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPersistenceFailureClassifier _classifier;
    private readonly ILogger _logger;

    public AttendanceRecordIngestion(
        IAttendanceRepository repository,
        IUnitOfWork unitOfWork,
        IPersistenceFailureClassifier? classifier = null,
        ILogger? logger = null)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _classifier = classifier ?? DefaultPersistenceFailureClassifier.Instance;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<AttendanceIngestionResult> IngestAsync(
        IReadOnlyList<AttendanceRecord> candidates,
        CancellationToken cancellationToken)
    {
        var duplicates = new Dictionary<(EmployeeId, DateTime), AttendanceRecordId>();

        var distinct = new List<AttendanceRecord>();
        var seen = new HashSet<(EmployeeId, DateTime)>();
        foreach (var candidate in candidates)
        {
            if (seen.Add((candidate.EmployeeId, candidate.CheckTime))) distinct.Add(candidate);
        }
        var inBatchDuplicates = candidates.Count - distinct.Count;

        var pending = distinct;
        for (int attempt = 0; ; attempt++)
        {
            var added = false;
            try
            {
                pending = await RemoveExistingAsync(pending, duplicates, cancellationToken);
                if (pending.Count == 0)
                {
                    return new AttendanceIngestionResult(Array.Empty<AttendanceRecord>(), duplicates, inBatchDuplicates, null);
                }

                await _repository.AddRangeAsync(pending, cancellationToken);
                added = true;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return new AttendanceIngestionResult(pending, duplicates, inBatchDuplicates, null);
            }
            catch (Exception ex)
            {
                // F03-07: no reintentar con entidades fallidas aún pendientes en el contexto.
                if (added)
                {
                    foreach (var record in pending) _repository.Detach(record);
                }

                var failure = _classifier.Classify(ex);
                if (failure.Kind == PersistenceFailureKind.Cancelled)
                {
                    throw;
                }

                if (failure.Kind == PersistenceFailureKind.UniqueViolation && attempt < MaxUniqueConflictRetries)
                {
                    _logger.LogInformation(
                        "Colisión de identidad de marcaje ({Constraint}); se confirma contra la base de datos y se reintenta ({Attempt}).",
                        failure.ConstraintName, attempt + 1);
                    continue;
                }

                _logger.LogError(ex,
                    "Fallo de persistencia {Kind} al ingerir {Count} marcajes; no se guardó ninguno y no se cuentan como duplicados.",
                    failure.Kind, pending.Count);
                return new AttendanceIngestionResult(Array.Empty<AttendanceRecord>(), duplicates, inBatchDuplicates, failure);
            }
        }
    }

    private async Task<List<AttendanceRecord>> RemoveExistingAsync(
        List<AttendanceRecord> pending,
        Dictionary<(EmployeeId, DateTime), AttendanceRecordId> duplicates,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0) return pending;

        var existing = (await _repository.GetByIdentitiesAsync(
            pending.Select(r => (r.EmployeeId, r.CheckTime)).ToList(),
            cancellationToken)) ?? Array.Empty<AttendanceRecord>();

        var existingByKey = new Dictionary<(EmployeeId, DateTime), AttendanceRecordId>();
        foreach (var record in existing)
        {
            existingByKey.TryAdd((record.EmployeeId, record.CheckTime), record.Id);
        }

        var remaining = new List<AttendanceRecord>();
        foreach (var record in pending)
        {
            if (existingByKey.TryGetValue((record.EmployeeId, record.CheckTime), out var existingId))
            {
                duplicates[(record.EmployeeId, record.CheckTime)] = existingId;
            }
            else
            {
                remaining.Add(record);
            }
        }

        return remaining;
    }
}
