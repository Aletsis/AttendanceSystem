using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Positions.Commands.DeletePosition;

public sealed record DeletePositionCommand(Guid Id) : IRequest<Result>;

public sealed class DeletePositionCommandHandler : IRequestHandler<DeletePositionCommand, Result>
{
    private readonly IPositionRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePositionCommandHandler(
        IPositionRepository repository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeletePositionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var positionId = PositionId.From(request.Id);
            
            var position = await _repository.GetByIdAsync(positionId, cancellationToken);
            if (position == null)
            {
                return Result.Failure("Puesto no encontrado.");
            }

            // Check if position is in use by any employee
            if (await _employeeRepository.IsPositionInUseAsync(positionId, cancellationToken))
            {
                return Result.Failure("No se puede eliminar el puesto porque hay empleados asignados a él.");
            }

            _repository.Delete(position);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
