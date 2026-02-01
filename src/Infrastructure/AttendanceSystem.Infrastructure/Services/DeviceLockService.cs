using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Services;

public class DeviceLockService : IDeviceLockService
{
    // Holds a semaphore for each device ID
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task ExecuteWithLockAsync(string deviceId, Func<Task> action, CancellationToken cancellationToken)
    {
        // Get or create the semaphore for this device
        // InitialCount: 1 (free), MaxCount: 1 (mutex)
        var semaphore = _locks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));

        // Wait to enter the semaphore
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await action();
        }
        finally
        {
            // Always release the semaphore
            semaphore.Release();
        }
    }
}
