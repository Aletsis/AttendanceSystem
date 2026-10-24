using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs;

[RequiresPermission(Permissions.SettingsManage)]
public sealed record SyncCloudLogsCommand : IRequest<Result<SyncCloudLogsResultDto>>;

public sealed record SyncCloudLogsResultDto(int DownloadedLogsCount, int UploadedLogsCount);

public sealed class SyncCloudLogsCommandHandler : IRequestHandler<SyncCloudLogsCommand, Result<SyncCloudLogsResultDto>>
{
    private readonly ICloudLogSyncService _cloudLogSyncService;
    private readonly ILogTransferService _logTransferService;
    private readonly IBranchRepository _branchRepository;
    private readonly ILogger<SyncCloudLogsCommandHandler> _logger;

    public SyncCloudLogsCommandHandler(
        ICloudLogSyncService cloudLogSyncService,
        ILogTransferService logTransferService,
        IBranchRepository branchRepository,
        ILogger<SyncCloudLogsCommandHandler> logger)
    {
        _cloudLogSyncService = cloudLogSyncService;
        _logTransferService = logTransferService;
        _branchRepository = branchRepository;
        _logger = logger;
    }

    public async Task<Result<SyncCloudLogsResultDto>> Handle(SyncCloudLogsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Reintentar subida de logs externos locales pendientes hacia la nube
            var uploadResult = await _logTransferService.RetryPendingTransfersAsync(cancellationToken);
            if (!uploadResult.IsSuccess)
            {
                _logger.LogError("Fallo durante la subida de logs externos pendientes: {Error}", uploadResult.Error);
                return Result<SyncCloudLogsResultDto>.Failure($"Fallo en subida a la nube: {uploadResult.Error}");
            }
            int uploadedCount = uploadResult.Value;

            // 2. Obtener los códigos de las sucursales locales (las que no son externas)
            var allBranches = await _branchRepository.GetAllAsync(cancellationToken);
            var localBranchCodes = allBranches
                .Where(b => !b.IsExternal)
                .Select(b => b.Code)
                .Distinct()
                .ToList();

            int downloadedCount = 0;
            if (localBranchCodes.Any())
            {
                var downloadResult = await _cloudLogSyncService.SyncPendingLogsForBranchesAsync(localBranchCodes, cancellationToken);
                if (!downloadResult.IsSuccess)
                {
                    _logger.LogError("Error al sincronizar descargas de la nube: {Error}", downloadResult.Error);
                    return Result<SyncCloudLogsResultDto>.Failure($"Fallo en descarga desde la nube: {downloadResult.Error}");
                }
                downloadedCount = downloadResult.Value;
            }

            return Result<SyncCloudLogsResultDto>.Success(new SyncCloudLogsResultDto(downloadedCount, uploadedCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar sincronización con la nube");
            return Result<SyncCloudLogsResultDto>.Failure($"Error en sincronización: {ex.Message}");
        }
    }
}
