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
            await _sender.Send(new ProcessDailyAttendanceCommand(claim.StartDate, claim.EndDate, EmployeeId: claim.EmployeeId), cancellationToken);
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

        // El resultado ya está confirmado: completar no debe cancelarse a medias.
        var completion = await _repository.CompleteAsync(claim.Id, consumerId, claim.Version, CancellationToken.None);
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
