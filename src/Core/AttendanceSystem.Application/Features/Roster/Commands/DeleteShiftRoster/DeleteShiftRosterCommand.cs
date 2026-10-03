using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Commands.DeleteShiftRoster;

public record DeleteShiftRosterCommand(Guid Id) : IRequest<bool>;

public class DeleteShiftRosterCommandHandler : IRequestHandler<DeleteShiftRosterCommand, bool>
{
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteShiftRosterCommandHandler(
        IShiftRosterRepository rosterRepository,
        IUnitOfWork unitOfWork)
    {
        _rosterRepository = rosterRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteShiftRosterCommand request, CancellationToken cancellationToken)
    {
        var roster = await _rosterRepository.GetByIdAsync(ShiftRosterId.From(request.Id), cancellationToken);
        if (roster == null) return false;

        _rosterRepository.Remove(roster);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
