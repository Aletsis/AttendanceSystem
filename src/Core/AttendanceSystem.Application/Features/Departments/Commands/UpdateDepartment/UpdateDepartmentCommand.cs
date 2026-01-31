using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Departments.Commands.UpdateDepartment;

public sealed record UpdateDepartmentCommand(
    Guid Id,
    string Name,
    string? Description,
    List<Guid> PositionIds = null) : IRequest<Result<Unit>>;

public sealed class UpdateDepartmentCommandHandler : IRequestHandler<UpdateDepartmentCommand, Result<Unit>>
{
    private readonly IDepartmentRepository _repository;
    private readonly IPositionRepository _positionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDepartmentCommandHandler(IDepartmentRepository repository, IPositionRepository positionRepository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _positionRepository = positionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit>> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var department = await _repository.GetByIdAsync(DepartmentId.From(request.Id), cancellationToken);
            if (department is null)
                return Result<Unit>.Failure("Departamento no encontrado");

            department.Update(
                request.Name,
                request.Description);

            _repository.Update(department);

            // Update Positions
            // Update Positions
             if (request.PositionIds != null)
            {
                var positionsToAssign = new List<AttendanceSystem.Domain.Aggregates.PositionAggregate.Position>();
                foreach (var positionId in request.PositionIds)
                {
                    var position = await _positionRepository.GetByIdAsync(Domain.ValueObjects.PositionId.From(positionId), cancellationToken);
                     if (position != null)
                    {
                        positionsToAssign.Add(position);
                    }
                }
                department.SetPositions(positionsToAssign);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
             return Result<Unit>.Failure(ex.Message);
        }
    }
}
