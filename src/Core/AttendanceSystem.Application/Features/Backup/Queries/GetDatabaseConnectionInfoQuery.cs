using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Backup.Queries;

public record GetDatabaseConnectionInfoQuery : IRequest<DatabaseConnectionInfoDto>;

public class GetDatabaseConnectionInfoQueryHandler : IRequestHandler<GetDatabaseConnectionInfoQuery, DatabaseConnectionInfoDto>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<GetDatabaseConnectionInfoQueryHandler> _logger;

    public GetDatabaseConnectionInfoQueryHandler(
        IBackupService backupService,
        ILogger<GetDatabaseConnectionInfoQueryHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public Task<DatabaseConnectionInfoDto> Handle(GetDatabaseConnectionInfoQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Obteniendo información de conexión de base de datos activa para respaldos");
        var info = _backupService.GetDatabaseConnectionInfo();
        return Task.FromResult(info);
    }
}
