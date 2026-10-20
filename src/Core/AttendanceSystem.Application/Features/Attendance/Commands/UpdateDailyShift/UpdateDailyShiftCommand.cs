using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;

public sealed record UpdateDailyShiftCommand(
    string EmployeeId,
    DateOnly Date,
    Guid ShiftId) : IRequest<Result>;

/// <summary>
/// Cambia el turno de una jornada como decisión manual persistente y la reconstruye con el motor
/// común de recálculo (F02-05). Así la selección de marcajes, la pertenencia y las jornadas
/// dependientes se resuelven igual que en cualquier otro recálculo.
/// </summary>
public sealed class UpdateDailyShiftCommandHandler : IRequestHandler<UpdateDailyShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly ISender _sender;
    private readonly ILogger<UpdateDailyShiftCommandHandler> _logger;
    private readonly IAttendanceCalculationLockService _lockService;

    public UpdateDailyShiftCommandHandler(
        IShiftRepository shiftRepo,
        IUnitOfWork unitOfWork,
        IEmployeeRepository employeeRepo,
        IAttendanceAdjustmentRepository adjustmentRepo,
        ISender sender,
        ILogger<UpdateDailyShiftCommandHandler> logger,
        IAttendanceCalculationLockService lockService)
    {
        _shiftRepo = shiftRepo;
        _unitOfWork = unitOfWork;
        _employeeRepo = employeeRepo;
        _adjustmentRepo = adjustmentRepo;
        _sender = sender;
        _logger = logger;
        _lockService = lockService;
    }

    public async Task<Result> Handle(UpdateDailyShiftCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var shiftId = ShiftId.From(request.ShiftId);

        var employee = await _employeeRepo.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null) return Result.Failure("Empleado no encontrado.");

        var shift = await _shiftRepo.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            return Result.Failure("El turno seleccionado no existe.");
        }

        var date = request.Date.ToDateTime(TimeOnly.MinValue);

        // Cargar o crear el ajuste manual persistente bajo el bloqueo del empleado (F03-03): la decisión
        // sobrevive a cualquier recálculo posterior y no se pierde frente a uno concurrente.
        await ExclusiveAttendanceWrite.RunAsync(
            _unitOfWork,
            _lockService,
            new[] { employeeId },
            async () =>
            {
                var adjustment = await _adjustmentRepo.GetByEmployeeAndDateAsync(employeeId, date, cancellationToken);
                if (adjustment == null)
                {
                    adjustment = AttendanceAdjustment.Create(employeeId, date, "Usuario RH");
                    adjustment.SetShiftOverride(shiftId, "Usuario RH", "Cambio manual de turno");
                    await _adjustmentRepo.AddAsync(adjustment, cancellationToken);
                }
                else
                {
                    adjustment.SetShiftOverride(shiftId, "Usuario RH", "Cambio manual de turno");
                    _adjustmentRepo.Update(adjustment);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Success();
            },
            cancellationToken);

        try
        {
            await _sender.Send(new ProcessDailyAttendanceCommand(date, date, EmployeeId: employeeId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "El turno de {EmployeeId} para {Date:yyyy-MM-dd} se guardó, pero el recálculo falló.", employeeId.Value, date);
            return Result.Failure($"El turno se guardó, pero el recálculo de la jornada falló: {ex.Message}");
        }

        return Result.Success();
    }
}
