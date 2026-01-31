using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Positions.Commands.CreatePosition;

public sealed record CreatePositionCommand(
    string Name,
    string? Description,
    decimal BaseSalary) : IRequest<Result<Guid>>;

public sealed class CreatePositionCommandHandler : IRequestHandler<CreatePositionCommand, Result<Guid>>
{
    private readonly IPositionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePositionCommandHandler(IPositionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreatePositionCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var position = Position.Create(
                request.Name,
                request.Description,
                request.BaseSalary);

            await _repository.AddAsync(position, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(position.Id.Value);
        }
        catch (Exception ex)
        {
             return Result<Guid>.Failure(ex.Message);
        }
    }
}
