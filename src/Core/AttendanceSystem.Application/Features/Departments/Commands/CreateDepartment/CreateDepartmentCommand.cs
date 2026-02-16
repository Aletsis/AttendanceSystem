using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Departments.Commands.CreateDepartment;

public sealed record CreateDepartmentCommand(
    string Name,
    string? Description,
    List<Guid>? PositionIds = null) : IRequest<Result<Guid>>;

public sealed class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, Result<Guid>>
{
    private readonly IDepartmentRepository _repository;
    private readonly IPositionRepository _positionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDepartmentCommandHandler(IDepartmentRepository repository, IPositionRepository positionRepository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _positionRepository = positionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var department = Department.Create(
                request.Name,
                request.Description);

            await _repository.AddAsync(department, cancellationToken);

            if (request.PositionIds != null && request.PositionIds.Any())
            {
                foreach (var positionId in request.PositionIds)
                {
                    var position = await _positionRepository.GetByIdAsync(Domain.ValueObjects.PositionId.From(positionId), cancellationToken);
                    if (position != null)
                    {
                        department.AddPosition(position);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(department.Id.Value);
        }
        catch (Exception ex)
        {
             return Result<Guid>.Failure(ex.Message);
        }
    }
}
