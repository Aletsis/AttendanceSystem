using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Backup.Commands;

public sealed record UpdateBackupDirectoryCommand(string BackupDirectory) : IRequest<Result<string>>;

public sealed class UpdateBackupDirectoryCommandHandler : IRequestHandler<UpdateBackupDirectoryCommand, Result<string>>
{
    private readonly ISystemConfigurationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateBackupDirectoryCommandHandler> _logger;

    public UpdateBackupDirectoryCommandHandler(
        ISystemConfigurationRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateBackupDirectoryCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<string>> Handle(UpdateBackupDirectoryCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BackupDirectory))
        {
            return Result<string>.Failure("El directorio de respaldos no puede estar vacío.");
        }

        try
        {
            var config = await _repository.GetConfigurationAsync(cancellationToken);
            if (config == null)
            {
                config = SystemConfiguration.CreateDefault();
                _repository.Add(config);
            }

            config.UpdateBackupDirectory(request.BackupDirectory.Trim());

            _repository.Update(config);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Directorio de respaldos actualizado a: {BackupDirectory}", config.BackupDirectory);
            return Result<string>.Success(config.BackupDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar el directorio de respaldos");
            return Result<string>.Failure($"Error al actualizar directorio: {ex.Message}");
        }
    }
}
