namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Provides a mechanism to acquire mutually exclusive locks for device operations.
/// Prevents concurrent downloads from the same device.
/// </summary>
public interface IDeviceLockService
{
    /// <summary>
    /// Executes the specified action within a lock for the given device ID.
    /// If the device is currently locked, this method waits until the lock is released.
    /// </summary>
    /// <param name="deviceId">The identifier of the device to lock.</param>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the wait operation.</param>
    Task ExecuteWithLockAsync(string deviceId, Func<Task> action, CancellationToken cancellationToken);
}
