using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Branches.Commands.CreateBranch;

public sealed record CreateBranchCommand(
    string Name,
    string? Description,
    string? Address) : IRequest<Result<Guid>>;

public sealed class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, Result<Guid>>
{
    private readonly IBranchRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBranchCommandHandler(IBranchRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var branch = Branch.Create(
                request.Name,
                request.Description,
                request.Address);

            await _repository.AddAsync(branch, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(branch.Id.Value);
        }
        catch (Exception ex)
        {
             return Result<Guid>.Failure(ex.Message);
        }
    }
}
