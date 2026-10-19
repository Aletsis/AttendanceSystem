using System;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class AuthoritativeTimeZoneResolver : IAuthoritativeTimeZoneResolver
{
    private const string GlobalDefaultTimeZone = "America/Mexico_City";
    private readonly IDeviceRepository _deviceRepo;
    private readonly IBranchRepository _branchRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ISystemConfigurationRepository _systemConfigRepo;
    private readonly ILogger<AuthoritativeTimeZoneResolver> _logger;

    public AuthoritativeTimeZoneResolver(
        IDeviceRepository deviceRepo,
        IBranchRepository branchRepo,
        IEmployeeRepository employeeRepo,
        ISystemConfigurationRepository systemConfigRepo,
        ILogger<AuthoritativeTimeZoneResolver> logger)
    {
        _deviceRepo = deviceRepo;
        _branchRepo = branchRepo;
        _employeeRepo = employeeRepo;
        _systemConfigRepo = systemConfigRepo;
        _logger = logger;
    }

    public async Task<string> ResolveForDeviceAsync(DeviceId? deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId != null)
        {
            try
            {
                var device = await _deviceRepo.GetByIdAsync(deviceId, cancellationToken);
                if (device != null && !string.IsNullOrWhiteSpace(device.TimeZoneId))
                {
                    return device.TimeZoneId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al resolver zona horaria del dispositivo {DeviceId}. Intentando nivel sucursal/sistema.", deviceId.Value);
            }
        }

        return await GetSystemDefaultTimeZoneAsync(cancellationToken);
    }

    public async Task<string> ResolveForEmployeeAsync(EmployeeId? employeeId, CancellationToken cancellationToken = default)
    {
        if (employeeId != null)
        {
            try
            {
                var employee = await _employeeRepo.GetByIdAsync(employeeId, cancellationToken);
                if (employee?.BranchId != null)
                {
                    var branchTz = await ResolveForBranchAsync(employee.BranchId, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(branchTz))
                    {
                        return branchTz;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al resolver zona horaria del empleado {EmployeeId}.", employeeId.Value);
            }
        }

        return await GetSystemDefaultTimeZoneAsync(cancellationToken);
    }

    public async Task<string> ResolveForBranchAsync(BranchId? branchId, CancellationToken cancellationToken = default)
    {
        if (branchId != null)
        {
            try
            {
                var branch = await _branchRepo.GetByIdAsync(branchId, cancellationToken);
                if (branch != null && !string.IsNullOrWhiteSpace(branch.TimeZoneId))
                {
                    return branch.TimeZoneId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al resolver zona horaria de la sucursal {BranchId}.", branchId.Value);
            }
        }

        return await GetSystemDefaultTimeZoneAsync(cancellationToken);
    }

    public async Task<string> GetSystemDefaultTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _systemConfigRepo.GetConfigurationAsync(cancellationToken);
            if (config != null && !string.IsNullOrWhiteSpace(config.DefaultTimeZoneId))
            {
                return config.DefaultTimeZoneId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al obtener zona horaria de SystemConfiguration. Usando fallback '{Fallback}'.", GlobalDefaultTimeZone);
        }

        return GlobalDefaultTimeZone;
    }
}
