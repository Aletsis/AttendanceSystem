using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.DownloadLogs.Queries.GetDownloadLogs;

public record GetDownloadLogsQuery(DateTime? FromDate, DateTime? ToDate) : IRequest<Result<IEnumerable<DownloadLog>>>;

public class GetDownloadLogsQueryHandler : IRequestHandler<GetDownloadLogsQuery, Result<IEnumerable<DownloadLog>>>
{
    private readonly IDownloadLogRepository _repository;

    public GetDownloadLogsQueryHandler(IDownloadLogRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IEnumerable<DownloadLog>>> Handle(GetDownloadLogsQuery request, CancellationToken cancellationToken)
    {
        IEnumerable<DownloadLog> logs;

        if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            logs = await _repository.GetByDateRangeAsync(request.FromDate.Value, request.ToDate.Value);
        }
        else
        {
            logs = await _repository.GetRecentAsync(100);
        }

        return Result<IEnumerable<DownloadLog>>.Success(logs);
    }
}
