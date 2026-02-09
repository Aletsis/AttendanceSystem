using MediatR;

namespace AttendanceSystem.Application.Features.Backup.Commands;

public record DeleteBackupCommand(string BackupFilePath) : IRequest<bool>;

public class DeleteBackupCommandHandler : IRequestHandler<DeleteBackupCommand, bool>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<DeleteBackupCommandHandler> _logger;

    public DeleteBackupCommandHandler(
        IBackupService backupService,
        ILogger<DeleteBackupCommandHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteBackupCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Eliminando respaldo: {FilePath}", request.BackupFilePath);
            var result = await _backupService.DeleteBackupAsync(request.BackupFilePath, cancellationToken);
            
            if (result)
            {
                _logger.LogInformation("Respaldo eliminado exitosamente");
            }
            else
            {
                _logger.LogWarning("No se pudo eliminar el respaldo");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar respaldo");
            return false;
        }
    }
}
