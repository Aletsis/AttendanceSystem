using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;

public sealed record GetShiftsQuery : IRequest<Result<IEnumerable<ShiftDto>>>;

public sealed class GetShiftsQueryHandler : IRequestHandler<GetShiftsQuery, Result<IEnumerable<ShiftDto>>>
{
    private readonly IShiftQueries _shiftQueries;

    public GetShiftsQueryHandler(IShiftQueries shiftQueries)
    {
        _shiftQueries = shiftQueries;
    }

    public async Task<Result<IEnumerable<ShiftDto>>> Handle(GetShiftsQuery request, CancellationToken cancellationToken)
    {
        var shifts = await _shiftQueries.GetAllShiftsAsync(cancellationToken);
        return Result<IEnumerable<ShiftDto>>.Success(shifts);
    }
}
