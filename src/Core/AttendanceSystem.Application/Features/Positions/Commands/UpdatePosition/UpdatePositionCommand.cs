using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Positions.Commands.UpdatePosition;

public sealed record UpdatePositionCommand(
    Guid Id,
    string Name,
    string? Description,
    decimal BaseSalary) : IRequest<Result<Unit>>;

public sealed class UpdatePositionCommandHandler : IRequestHandler<UpdatePositionCommand, Result<Unit>>
{
    private readonly IPositionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePositionCommandHandler(IPositionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit>> Handle(UpdatePositionCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var position = await _repository.GetByIdAsync(PositionId.From(request.Id), cancellationToken);
            if (position is null)
                return Result<Unit>.Failure("Puesto no encontrado");

            position.Update(
                request.Name,
                request.Description,
                request.BaseSalary);

            _repository.Update(position);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
             return Result<Unit>.Failure(ex.Message);
        }
    }
}
