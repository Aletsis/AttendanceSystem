using AttendanceSystem.Application.DTOs;
using MediatR;

namespace AttendanceSystem.Application.Features.Backup.Commands;

public record RestoreBackupCommand(string BackupFilePath) : IRequest<RestoreResultDto>;

public class RestoreBackupCommandHandler : IRequestHandler<RestoreBackupCommand, RestoreResultDto>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<RestoreBackupCommandHandler> _logger;

    public RestoreBackupCommandHandler(
        IBackupService backupService,
        ILogger<RestoreBackupCommandHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<RestoreResultDto> Handle(RestoreBackupCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Iniciando restauración desde: {FilePath}", request.BackupFilePath);

            // Validar el archivo primero
            var isValid = await _backupService.ValidateBackupAsync(request.BackupFilePath, cancellationToken);
            if (!isValid)
            {
                return new RestoreResultDto
                {
                    Success = false,
                    Message = "El archivo de respaldo no es válido o está corrupto"
                };
            }

            var result = await _backupService.RestoreBackupAsync(request.BackupFilePath, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Restauración completada exitosamente");
            }
            else
            {
                _logger.LogWarning("Error al restaurar: {Message}", result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al restaurar respaldo");
            return new RestoreResultDto
            {
                Success = false,
                Message = $"Error inesperado: {ex.Message}"
            };
        }
    }
}
