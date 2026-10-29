using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Common;

/// <summary>Caso 18: la política de la hora de los relojes vigente, la misma para SDK, ADMS y la cuarentena.</summary>
public static class ClockPolicy
{
    /// <summary>La política configurada o, sin configuración, la predeterminada.</summary>
    public static async Task<PunchQuarantinePolicy> GetClockPolicyAsync(this ISystemConfigurationRepository? configuration, CancellationToken cancellationToken) =>
        configuration != null && await configuration.GetConfigurationAsync(cancellationToken) is { } config
            ? config.QuarantinePolicy
            : PunchQuarantinePolicy.Default;
}
