using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs;

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
            int uploadedCount = uploadResult.IsSuccess ? uploadResult.Value : 0;

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
                if (downloadResult.IsSuccess)
                {
                    downloadedCount = downloadResult.Value;
                }
                else
                {
                    _logger.LogWarning("Error al sincronizar descargas de la nube: {Error}", downloadResult.Error);
                }
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
