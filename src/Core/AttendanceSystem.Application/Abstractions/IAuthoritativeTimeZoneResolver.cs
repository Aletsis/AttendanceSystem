using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Resuelve la zona horaria autoritativa siguiendo la precedencia contractual (C-01):
/// 1. Device.TimeZoneId
/// 2. Branch.TimeZoneId
/// 3. SystemConfiguration.DefaultTimeZoneId
/// 4. Fallback: "America/Mexico_City" (nunca depende de TimeZoneInfo.Local del servidor).
/// </summary>
public interface IAuthoritativeTimeZoneResolver
{
    Task<string> ResolveForDeviceAsync(DeviceId? deviceId, CancellationToken cancellationToken = default);
    Task<string> ResolveForEmployeeAsync(EmployeeId? employeeId, CancellationToken cancellationToken = default);
    Task<string> ResolveForBranchAsync(BranchId? branchId, CancellationToken cancellationToken = default);
    Task<string> GetSystemDefaultTimeZoneAsync(CancellationToken cancellationToken = default);
}

public sealed class DefaultAuthoritativeTimeZoneResolver : IAuthoritativeTimeZoneResolver
{
    public static readonly IAuthoritativeTimeZoneResolver Instance = new DefaultAuthoritativeTimeZoneResolver();

    public Task<string> ResolveForDeviceAsync(DeviceId? deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult("America/Mexico_City");

    public Task<string> ResolveForEmployeeAsync(EmployeeId? employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult("America/Mexico_City");

    public Task<string> ResolveForBranchAsync(BranchId? branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult("America/Mexico_City");

    public Task<string> GetSystemDefaultTimeZoneAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("America/Mexico_City");
}
