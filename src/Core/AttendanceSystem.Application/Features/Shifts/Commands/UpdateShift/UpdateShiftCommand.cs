using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Commands.UpdateShift;

[RequiresPermission(Permissions.ShiftsManage)]
public sealed record UpdateShiftCommand(
    Guid Id,
    string Name,
    TimeSpan StartTime,
    int ToleranceMinutes,
    TimeSpan WorkHours,
    ShiftType ShiftType,
    IEnumerable<AttendanceSystem.Application.DTOs.ShiftDayDto>? Days = null,
    int LunchBreakMinutes = 0,
    bool RoundingsEnabled = false,
    int RoundingInterval = 0,
    TimeSpan? FlexWindowEndTime = null,
    TimeSpan? WeeklyWorkHours = null,
    TimeSpan? SecondBlockStartTime = null,
    TimeSpan? SecondBlockEndTime = null,
    int? SecondBlockToleranceMinutes = null,
    PunchTrackingMode PunchTrackingMode = PunchTrackingMode.SingleInterval,
    bool HasEntryWindow = true,
    int? PaidLunchMinutes = null,
    bool UpdatePaidLunchMinutes = false) : IRequest<Result>;

public sealed class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateShiftCommandHandler(IShiftRepository shiftRepository, IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
    {
        var shiftId = ShiftId.From(request.Id);
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);

        if (shift is null)
        {
            return Result.Failure("Turno no encontrado");
        }

        try
        {
            var builtDays = await MixedShiftDays.BuildAsync(request.ShiftType, request.Days, _shiftRepository, shiftId, cancellationToken);
            if (builtDays.IsFailure) return Result.Failure(builtDays.Error);
            var days = builtDays.Value;

            // Un horario usado por un turno mixto no puede volverse mixto ni rotativo.
            if (request.ShiftType is ShiftType.Mixto or ShiftType.Rotativo && shift.ShiftType is not (ShiftType.Mixto or ShiftType.Rotativo))
            {
                var usedBy = await MixedShiftsUsingAsync(shiftId, cancellationToken);
                if (usedBy.Count > 0)
                    return Result.Failure($"El horario se usa en los turnos mixtos {string.Join(", ", usedBy)}; no puede convertirse en {request.ShiftType}.");
            }

            shift.Update(
                request.Name,
                request.StartTime,
                request.ToleranceMinutes,
                request.WorkHours,
                request.ShiftType,
                days,
                lunchBreakMinutes: request.LunchBreakMinutes,
                roundingsEnabled: request.RoundingsEnabled,
                roundingInterval: request.RoundingInterval,
                flexWindowEndTime: request.FlexWindowEndTime,
                weeklyWorkHours: request.WeeklyWorkHours,
                secondBlockStartTime: request.SecondBlockStartTime,
                secondBlockEndTime: request.SecondBlockEndTime,
                secondBlockToleranceMinutes: request.SecondBlockToleranceMinutes,
                punchTrackingMode: request.PunchTrackingMode,
                hasEntryWindow: request.HasEntryWindow);
            // Los clientes que no conocen el campo (UpdatePaidLunchMinutes = false) no lo modifican.
            if (request.UpdatePaidLunchMinutes) shift.SetPaidLunchMinutes(request.PaidLunchMinutes);

            _shiftRepository.Update(shift);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private async Task<List<string>> MixedShiftsUsingAsync(ShiftId shiftId, CancellationToken cancellationToken) =>
        (await _shiftRepository.GetAllAsync(cancellationToken))
            .Where(s => s.ShiftType == ShiftType.Mixto && s.Days.Any(d => d.DayShiftId == shiftId))
            .Select(s => s.Name)
            .ToList();
}
