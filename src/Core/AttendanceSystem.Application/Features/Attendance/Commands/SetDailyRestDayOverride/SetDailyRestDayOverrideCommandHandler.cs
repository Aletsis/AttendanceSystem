using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

public class SetDailyRestDayOverrideCommandHandler : IRequestHandler<SetDailyRestDayOverrideCommand, bool>
{
    private readonly IDailyAttendanceRepository _repository;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;

    public SetDailyRestDayOverrideCommandHandler(
        IDailyAttendanceRepository repository,
        IAttendanceAdjustmentRepository adjustmentRepo,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService)
    {
        _repository = repository;
        _adjustmentRepo = adjustmentRepo;
        _unitOfWork = unitOfWork;
        _lockService = lockService;
    }

    public async Task<bool> Handle(SetDailyRestDayOverrideCommand request, CancellationToken cancellationToken)
    {
        var id = DailyAttendanceId.From(request.DailyAttendanceId);
        var located = await _repository.GetByIdAsync(id, cancellationToken);
        if (located == null) return false;

        var employeeId = located.EmployeeId;
        var date = located.Date;

        // F03-03: bajo el bloqueo del empleado se relee la jornada. Si un recálculo la reemplazó entre la
        // lectura de pantalla y este comando, el identificador ya no existe y se informa como no aplicado.
        return await ExclusiveAttendanceWrite.RunAsync(
            _unitOfWork,
            _lockService,
            new[] { employeeId },
            () => ApplyAsync(id, employeeId, date, request.IsRestDay, cancellationToken),
            applied => applied,
            cancellationToken);
    }

    private async Task<bool> ApplyAsync(DailyAttendanceId id, EmployeeId employeeId, DateTime date, bool isRestDay, CancellationToken cancellationToken)
    {
        var attendance = await _repository.GetByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        if (attendance == null || attendance.Id != id) return false;

        attendance.SetRestDayOverride(isRestDay);

        var adjustment = await _adjustmentRepo.GetByEmployeeAndDateAsync(attendance.EmployeeId, attendance.Date, cancellationToken);
        if (adjustment == null)
        {
            adjustment = AttendanceAdjustment.Create(attendance.EmployeeId, attendance.Date, "Usuario RH");
            adjustment.SetRestDayOverride(isRestDay, "Usuario RH", "Ajuste manual de descanso");
            await _adjustmentRepo.AddAsync(adjustment, cancellationToken);
        }
        else
        {
            adjustment.SetRestDayOverride(isRestDay, "Usuario RH", "Ajuste manual de descanso");
            _adjustmentRepo.Update(adjustment);
        }

        _repository.Update(attendance);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
