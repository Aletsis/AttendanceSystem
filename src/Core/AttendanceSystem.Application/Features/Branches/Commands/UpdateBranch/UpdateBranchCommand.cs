using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Branches.Commands.UpdateBranch;

public sealed record UpdateBranchCommand(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    bool IsExternal,
    string? ExternalHost) : IRequest<Result<Unit>>;

public sealed class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, Result<Unit>>
{
    private readonly IBranchRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBranchCommandHandler(IBranchRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit>> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var branch = await _repository.GetByIdAsync(BranchId.From(request.Id), cancellationToken);
            if (branch is null)
                return Result<Unit>.Failure("Sucursal no encontrada");

            branch.Update(
                request.Code,
                request.Name,
                request.Address,
                request.IsExternal,
                request.ExternalHost);

            _repository.Update(branch);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
             return Result<Unit>.Failure(ex.Message);
        }
    }
}
