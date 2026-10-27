using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Commands.DeleteDevice;

public enum DeleteDeviceOutcome
{
    /// <summary>Sin historial: el reloj se borró.</summary>
    Deleted,
    /// <summary>Con historial: se desactivó (no se descarga ni se le envían empleados) y sus checadas quedan intactas.</summary>
    Deactivated
}

/// <summary>
/// Elimina un reloj. Las checadas de un reloj nunca se borran, así que si tiene historial sólo se desactiva.
/// </summary>
[RequiresPermission(Permissions.DevicesManage)]
public sealed record DeleteDeviceCommand(Guid Id) : IRequest<Result<DeleteDeviceOutcome>>;

public sealed class DeleteDeviceCommandHandler : IRequestHandler<DeleteDeviceCommand, Result<DeleteDeviceOutcome>>
{
    private readonly IDeviceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDeviceCommandHandler(IDeviceRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DeleteDeviceOutcome>> Handle(DeleteDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var deviceId = DeviceId.From(request.Id.ToString());
            var device = await _repository.GetByIdAsync(deviceId, cancellationToken);
            if (device == null)
            {
                return Result<DeleteDeviceOutcome>.Failure("Dispositivo no encontrado.");
            }

            DeleteDeviceOutcome outcome;
            if (await _repository.HasHistoryAsync(deviceId, cancellationToken))
            {
                if (device.IsActive) device.Deactivate();
                await _repository.UpdateAsync(device, cancellationToken);
                outcome = DeleteDeviceOutcome.Deactivated;
            }
            else
            {
                _repository.Delete(device);
                outcome = DeleteDeviceOutcome.Deleted;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<DeleteDeviceOutcome>.Success(outcome);
        }
        catch (Exception ex)
        {
            return Result<DeleteDeviceOutcome>.Failure(ex.Message);
        }
    }
}
