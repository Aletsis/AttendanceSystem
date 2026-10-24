using AttendanceSystem.Application.Common;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;

[RequiresPermission(Permissions.ShiftsManage)]
public sealed record DeleteShiftCommand(ShiftId ShiftId) : IRequest<Result>;
