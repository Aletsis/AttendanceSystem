using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Backup.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Blazor.Server.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize(Roles = "Administrador")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IMediator _mediator;
    private readonly ILogger<BackupController> _logger;

    public BackupController(
        IBackupService backupService,
        IMediator mediator,
        ILogger<BackupController> logger)
    {
        _backupService = backupService;
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Descarga un archivo de respaldo del servidor mediante streaming HTTP
    /// </summary>
    /// <param name="fileName">Nombre del archivo de respaldo</param>
    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> DownloadBackup(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("El nombre del archivo es requerido.");
        }

        var decodedFileName = Uri.UnescapeDataString(fileName);
        _logger.LogInformation("Solicitud de descarga de respaldo: {FileName}", decodedFileName);

        var filePath = await _backupService.GetBackupFilePathAsync(decodedFileName, HttpContext.RequestAborted);
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
        {
            _logger.LogWarning("Respaldo no encontrado para descarga: {FileName}", decodedFileName);
            return NotFound("El archivo de respaldo solicitado no existe o no se tiene acceso.");
        }

        var downloadName = Path.GetFileName(filePath);
        return PhysicalFile(filePath, "application/octet-stream", downloadName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Carga un archivo de respaldo al servidor
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(1024L * 1024L * 1024L)] // 1 GB
    public async Task<IActionResult> UploadBackup(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { Success = false, Message = "No se ha seleccionado ningún archivo para cargar." });
        }

        _logger.LogInformation("Recibiendo archivo de respaldo vía API: {FileName} ({Length} bytes)", file.FileName, file.Length);

        await using var stream = file.OpenReadStream();
        var command = new UploadBackupCommand(file.FileName, stream);
        var result = await _mediator.Send(command, HttpContext.RequestAborted);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }
}
