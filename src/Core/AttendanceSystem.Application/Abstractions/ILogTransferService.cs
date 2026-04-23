using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

namespace AttendanceSystem.Application.Abstractions;

public interface ILogTransferService
{
    Task<Result<bool>> TransferLogAsync(
        string host, 
        string employeeId, 
        DateTime checkTime, 
        int verifyMethod, 
        int checkType, 
        CancellationToken cancellationToken = default);
}
