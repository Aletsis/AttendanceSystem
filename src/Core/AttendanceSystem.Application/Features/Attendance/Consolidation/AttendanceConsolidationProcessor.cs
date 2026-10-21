using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Consolidation;

public enum ConsolidationOutcome
{
    Completed,
    Requeued,
    LeaseLost,
    Failed,
    Released
}

/// <summary>
/// Procesa una solicitud reclamada (F04-03 / F04-05 / F04-06): recalcula el rango capturado al
/// reclamar y la completa sólo si su versión no cambió. Un fallo real cuenta intento con backoff; la
/// cancelación o el mantenimiento la liberan sin contarlo. El recálculo es idempotente, así que
/// repetir una solicitud cuyo resultado ya se confirmó no altera los datos.
/// </summary>
public sealed class AttendanceConsolidationProcessor
{
    private readonly IAttendanceConsolidationRepository _repository;
    private readonly ISender _sender;
    private readonly ILogger<AttendanceConsolidationProcessor> _logger;

    public AttendanceConsolidationProcessor(
        IAttendanceConsolidationRepository repository,
        ISender sender,
        ILogger<AttendanceConsolidationProcessor> logger)
    {
        _repository = repository;
        _sender = sender;
        _logger = logger;
    }

    public async Task<ConsolidationOutcome> ProcessAsync(
        ConsolidationClaim claim,
        string consumerId,
        ConsolidationOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            // F07-02: una solicitud acumulada puede superar el máximo de un recálculo; se procesa por tramos.
            foreach (var (start, end) in AttendanceSystem.Domain.Services.DateRangePolicy.Slices(claim.StartDate, claim.EndDate, AttendanceSystem.Domain.Services.DateRangePolicy.MaxRecalculationDays))
            {
                await _sender.Send(new ProcessDailyAttendanceCommand(start, end, EmployeeId: claim.EmployeeId), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _repository.ReleaseAsync(claim.Id, consumerId, CancellationToken.None);
            throw;
        }
        catch (DatabaseMaintenanceException ex)
        {
            _logger.LogInformation("Consolidación de {EmployeeId} pospuesta por mantenimiento: {Message}", claim.EmployeeId.Value, ex.Message);
            await _repository.ReleaseAsync(claim.Id, consumerId, CancellationToken.None);
            return ConsolidationOutcome.Released;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Consolidación de {EmployeeId} ({Start:yyyy-MM-dd}..{End:yyyy-MM-dd}) falló en el intento {Attempt}.",
                claim.EmployeeId.Value, claim.StartDate, claim.EndDate, claim.Attempts + 1);
            await _repository.FailAsync(claim.Id, consumerId, $"{ex.GetType().Name}: {ex.Message}", options.MaxAttempts, options.BaseBackoff, CancellationToken.None);
            return ConsolidationOutcome.Failed;
        }

        return await CompleteClaimAsync(claim, consumerId);
    }

    /// <summary>
    /// F07-08: procesa juntas las solicitudes reclamadas. Las del mismo rango se recalculan en un solo comando (que
    /// internamente usa lotes); cada solicitud conserva su resultado: completar con su versión, o contar un intento
    /// sólo para el empleado que falló. Ante un error que no identifica al empleado, el grupo se procesa uno por uno.
    /// </summary>
    public async Task<IReadOnlyList<ConsolidationOutcome>> ProcessBatchAsync(
        IReadOnlyList<ConsolidationClaim> claims,
        string consumerId,
        ConsolidationOptions options,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<ConsolidationOutcome>();
        var groups = claims.GroupBy(c => (Start: c.StartDate.Date, End: c.EndDate.Date)).ToList();
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g].ToList();
            if (group.Count == 1)
            {
                outcomes.Add(await ProcessAsync(group[0], consumerId, options, cancellationToken));
                continue;
            }

            var failed = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var ids = group.Select(c => c.EmployeeId).ToList();
                foreach (var (start, end) in AttendanceSystem.Domain.Services.DateRangePolicy.Slices(groups[g].Key.Start, groups[g].Key.End, AttendanceSystem.Domain.Services.DateRangePolicy.MaxRecalculationDays))
                {
                    try
                    {
                        await _sender.Send(new ProcessDailyAttendanceCommand(start, end, EmployeeIds: ids), cancellationToken);
                    }
                    catch (AttendanceRecalculationException ex)
                    {
                        foreach (var unit in ex.FailedUnits) failed.TryAdd(unit.EmployeeId, $"{unit.ErrorType}: {unit.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                foreach (var claim in groups.Skip(g).SelectMany(x => x))
                {
                    await _repository.ReleaseAsync(claim.Id, consumerId, CancellationToken.None);
                }
                throw;
            }
            catch (DatabaseMaintenanceException ex)
            {
                _logger.LogInformation("Consolidación de {Count} empleados pospuesta por mantenimiento: {Message}", group.Count, ex.Message);
                foreach (var claim in group)
                {
                    await _repository.ReleaseAsync(claim.Id, consumerId, CancellationToken.None);
                    outcomes.Add(ConsolidationOutcome.Released);
                }
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falló la consolidación conjunta de {Count} empleados; se procesan uno por uno.", group.Count);
                foreach (var claim in group)
                {
                    outcomes.Add(await ProcessAsync(claim, consumerId, options, cancellationToken));
                }
                continue;
            }

            // El resultado ya está confirmado: completar no debe cancelarse a medias. Las completadas van en una sola
            // sentencia (una confirmación); las fallidas cuentan su intento.
            var succeeded = group.Where(c => !failed.ContainsKey(c.EmployeeId.Value)).ToList();
            var completions = await _repository.CompleteManyAsync(succeeded.Select(c => (c.Id, c.Version)).ToList(), consumerId, CancellationToken.None);
            foreach (var claim in group)
            {
                if (failed.TryGetValue(claim.EmployeeId.Value, out var error))
                {
                    _logger.LogError("Consolidación de {EmployeeId} falló en el intento {Attempt}: {Error}", claim.EmployeeId.Value, claim.Attempts + 1, error);
                    await _repository.FailAsync(claim.Id, consumerId, error, options.MaxAttempts, options.BaseBackoff, CancellationToken.None);
                    outcomes.Add(ConsolidationOutcome.Failed);
                }
                else
                {
                    outcomes.Add(ToOutcome(claim, completions.TryGetValue(claim.Id, out var completion) ? completion : ConsolidationCompletion.LeaseLost));
                }
            }
        }

        foreach (var outcome in outcomes)
        {
            AttendanceSystem.Application.Common.AttendanceTelemetry.ConsolidationClaims.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        }
        return outcomes;
    }

    private async Task<ConsolidationOutcome> CompleteClaimAsync(ConsolidationClaim claim, string consumerId)
    {
        // El resultado ya está confirmado: completar no debe cancelarse a medias.
        var completion = await _repository.CompleteAsync(claim.Id, consumerId, claim.Version, CancellationToken.None);
        return ToOutcome(claim, completion);
    }

    private ConsolidationOutcome ToOutcome(ConsolidationClaim claim, ConsolidationCompletion completion)
    {
        switch (completion)
        {
            case ConsolidationCompletion.Requeued:
                _logger.LogInformation(
                    "La solicitud de {EmployeeId} se amplió durante el cálculo; queda pendiente con el rango acumulado.",
                    claim.EmployeeId.Value);
                return ConsolidationOutcome.Requeued;
            case ConsolidationCompletion.LeaseLost:
                _logger.LogWarning(
                    "El lease de la solicitud {RequestId} venció durante el cálculo; la completará el consumidor que la reclamó.",
                    claim.Id);
                return ConsolidationOutcome.LeaseLost;
            default:
                return ConsolidationOutcome.Completed;
        }
    }
}
