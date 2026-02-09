using AttendanceSystem.Application.DTOs;
using MediatR;

namespace AttendanceSystem.Application.Features.Backup.Commands;

public record CreateBackupCommand(
    string BackupType, // "Full" o "DatabaseOnly"
    string? Description = null
) : IRequest<BackupResultDto>;

public class CreateBackupCommandHandler : IRequestHandler<CreateBackupCommand, BackupResultDto>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<CreateBackupCommandHandler> _logger;

    public CreateBackupCommandHandler(
        IBackupService backupService,
        ILogger<CreateBackupCommandHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<BackupResultDto> Handle(CreateBackupCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Iniciando creación de respaldo tipo: {BackupType}", request.BackupType);

            BackupResultDto result = request.BackupType.ToLower() switch
            {
                "full" => await _backupService.CreateFullBackupAsync(request.Description, cancellationToken),
                "databaseonly" => await _backupService.CreateDatabaseBackupAsync(request.Description, cancellationToken),
                _ => new BackupResultDto
                {
                    Success = false,
                    Message = $"Tipo de respaldo no válido: {request.BackupType}"
                }
            };

            if (result.Success)
            {
                _logger.LogInformation("Respaldo creado exitosamente: {FilePath}", result.BackupFilePath);
            }
            else
            {
                _logger.LogWarning("Error al crear respaldo: {Message}", result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear respaldo");
            return new BackupResultDto
            {
                Success = false,
                Message = $"Error inesperado: {ex.Message}"
            };
        }
    }
}
