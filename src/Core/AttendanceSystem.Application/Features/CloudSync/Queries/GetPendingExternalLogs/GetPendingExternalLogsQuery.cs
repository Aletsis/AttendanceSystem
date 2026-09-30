using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.CloudSync.Queries.GetPendingExternalLogs;

public sealed record ExternalAttendanceLogDto(
    Guid Id,
    string BranchCode,
    string EmployeeId,
    DateTime CheckTime,
    int VerifyMethod,
    int CheckType,
    string? SourceDevice,
    string Status,
    int RetryCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? TransferredAt);

public sealed record GetPendingExternalLogsQuery(int Take = 50) : IRequest<Result<IReadOnlyList<ExternalAttendanceLogDto>>>;

public sealed class GetPendingExternalLogsQueryHandler : IRequestHandler<GetPendingExternalLogsQuery, Result<IReadOnlyList<ExternalAttendanceLogDto>>>
{
    private readonly IExternalAttendanceLogRepository _repository;

    public GetPendingExternalLogsQueryHandler(IExternalAttendanceLogRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ExternalAttendanceLogDto>>> Handle(GetPendingExternalLogsQuery request, CancellationToken cancellationToken)
    {
        var logs = await _repository.GetRecentLogsAsync(request.Take, cancellationToken);
        var dtos = logs.Select(x => new ExternalAttendanceLogDto(
            x.Id,
            x.BranchCode,
            x.EmployeeId,
            x.CheckTime,
            x.VerifyMethod,
            x.CheckType,
            x.SourceDevice,
            x.Status.Name,
            x.RetryCount,
            x.ErrorMessage,
            x.CreatedAt,
            x.TransferredAt
        )).ToList();

        return Result<IReadOnlyList<ExternalAttendanceLogDto>>.Success(dtos);
    }
}
