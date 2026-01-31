using AttendanceSystem.Application.Common;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;

public sealed record DeleteShiftCommand(ShiftId ShiftId) : IRequest<Result>;
