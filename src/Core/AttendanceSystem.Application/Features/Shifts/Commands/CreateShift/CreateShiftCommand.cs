using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;

using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Commands.CreateShift;

public sealed record CreateShiftCommand(
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
    bool HasEntryWindow = true) : IRequest<Result<Guid>>;

public sealed class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, Result<Guid>>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateShiftCommandHandler(IShiftRepository shiftRepository, IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var days = request.Days?.Select(d => new ShiftDay(
                d.DayOfWeek,
                d.StartTime,
                d.WorkHours,
                d.ShiftType
            )).ToList();

            var shift = Shift.Create(
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

            await _shiftRepository.AddAsync(shift, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(shift.Id.Value);
        }
        catch (Exception ex)
        {
            // Proporcionando un error genérico por ahora, confiando en el manejo global de excepciones o en la construcción de errores específicos si es necesario
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
