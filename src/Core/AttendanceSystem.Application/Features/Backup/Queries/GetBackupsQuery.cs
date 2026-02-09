using AttendanceSystem.Application.DTOs;
using MediatR;

namespace AttendanceSystem.Application.Features.Backup.Queries;

public record GetBackupsQuery : IRequest<IEnumerable<BackupDto>>;

public class GetBackupsQueryHandler : IRequestHandler<GetBackupsQuery, IEnumerable<BackupDto>>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<GetBackupsQueryHandler> _logger;

    public GetBackupsQueryHandler(
        IBackupService backupService,
        ILogger<GetBackupsQueryHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<IEnumerable<BackupDto>> Handle(GetBackupsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Obteniendo lista de respaldos disponibles");
            var backups = await _backupService.GetAvailableBackupsAsync(cancellationToken);
            _logger.LogInformation("Se encontraron {Count} respaldos", backups.Count());
            return backups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener lista de respaldos");
            return Enumerable.Empty<BackupDto>();
        }
    }
}
