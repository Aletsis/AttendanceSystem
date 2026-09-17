using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Backup.Commands;

public record UploadBackupCommand(
    string FileName,
    Stream ContentStream
) : IRequest<BackupResultDto>;

public class UploadBackupCommandHandler : IRequestHandler<UploadBackupCommand, BackupResultDto>
{
    private readonly IBackupService _backupService;
    private readonly ILogger<UploadBackupCommandHandler> _logger;

    public UploadBackupCommandHandler(
        IBackupService backupService,
        ILogger<UploadBackupCommandHandler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<BackupResultDto> Handle(UploadBackupCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Iniciando procesamiento de subida de respaldo: {FileName}", request.FileName);

            if (string.IsNullOrWhiteSpace(request.FileName) || request.ContentStream == null)
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = "El nombre de archivo y el flujo de datos son requeridos."
                };
            }

            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            if (extension != ".zip" && extension != ".backup" && extension != ".bak")
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = $"Formato de archivo no soportado ({extension}). Solo se admiten archivos .zip, .backup o .bak."
                };
            }

            var result = await _backupService.UploadBackupAsync(request.FileName, request.ContentStream, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Respaldo subido y validado exitosamente: {FilePath}", result.BackupFilePath);
            }
            else
            {
                _logger.LogWarning("Fallo en la subida del respaldo: {Message}", result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al subir el respaldo: {FileName}", request.FileName);
            return new BackupResultDto
            {
                Success = false,
                Message = $"Error inesperado al procesar el archivo: {ex.Message}"
            };
        }
    }
}
