using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using MediatR;

namespace AttendanceSystem.Application.Features.CloudSync.Commands.TestCloudConnection;

public sealed record TestCloudConnectionCommand(string? ConnectionString = null) : IRequest<Result<bool>>;

public sealed class TestCloudConnectionCommandHandler : IRequestHandler<TestCloudConnectionCommand, Result<bool>>
{
    private readonly ILogTransferService _logTransferService;

    public TestCloudConnectionCommandHandler(ILogTransferService logTransferService)
    {
        _logTransferService = logTransferService;
    }

    public async Task<Result<bool>> Handle(TestCloudConnectionCommand request, CancellationToken cancellationToken)
    {
        return await _logTransferService.TestConnectionAsync(request.ConnectionString, cancellationToken);
    }
}
