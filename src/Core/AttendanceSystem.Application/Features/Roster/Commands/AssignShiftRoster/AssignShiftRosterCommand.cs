using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Commands.AssignShiftRoster;

[RequiresPermission(Permissions.RosterManage)]
public record AssignShiftRosterCommand(
    string EmployeeId,
    DateTime Date,
    Guid? ShiftId,
    bool IsRestDay,
    string? Notes = null) : IRequest<Guid>;

public class AssignShiftRosterCommandHandler : IRequestHandler<AssignShiftRosterCommand, Guid>
{
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceConsolidationRepository? _consolidations;
    private readonly IConsolidationWakeup? _wakeup;

    public AssignShiftRosterCommandHandler(
        IShiftRosterRepository rosterRepository,
        IUnitOfWork unitOfWork,
        IAttendanceConsolidationRepository? consolidations = null,
        IConsolidationWakeup? wakeup = null)
    {
        _rosterRepository = rosterRepository;
        _unitOfWork = unitOfWork;
        _consolidations = consolidations;
        _wakeup = wakeup;
    }

    /// <summary>Caso 15: cambiar el roster de un día ya transcurrido (o de hoy) recalcula ese día.</summary>
    private async Task RecalculateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken)
    {
        if (_consolidations == null || date.Date > DateTime.Today) return;
        await _consolidations.RequestAsync(employeeId, date.Date, date.Date, cancellationToken);
        _wakeup?.Signal();
    }

    public async Task<Guid> Handle(AssignShiftRosterCommand request, CancellationToken cancellationToken)
    {
        var empId = EmployeeId.From(request.EmployeeId);
        var shiftId = request.ShiftId.HasValue ? ShiftId.From(request.ShiftId.Value) : null;

        var existing = await _rosterRepository.GetByEmployeeAndDateAsync(empId, request.Date.Date, cancellationToken);
        if (existing != null)
        {
            existing.Update(shiftId, request.IsRestDay, request.Notes);
            await _rosterRepository.UpdateAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RecalculateAsync(empId, request.Date, cancellationToken);
            return existing.Id.Value;
        }

        var roster = ShiftRoster.Create(
            empId,
            request.Date.Date,
            shiftId,
            request.IsRestDay,
            request.Notes);

        await _rosterRepository.AddAsync(roster, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await RecalculateAsync(empId, request.Date, cancellationToken);
        return roster.Id.Value;
    }
}
