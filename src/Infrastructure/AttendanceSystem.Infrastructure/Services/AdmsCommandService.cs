using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Infrastructure.Services;

public class AdmsCommandService : IAdmsCommandService
{
    private record QueuedCommand(string CommandText, Guid? DownloadLogId);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueuedCommand>> _commandQueues = new();

    // Map CommandID (sent to device) -> DownloadLogId
    private readonly ConcurrentDictionary<string, Guid> _pendingExecutions = new();

    // SerialNumber -> TaskCompletionSource<DeviceInfoDto?>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DeviceInfoDto?>> _deviceInfoWaiters = new(StringComparer.OrdinalIgnoreCase);

    public void EnqueueCommand(string serialNumber, string command, Guid? downloadLogId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return;

        var queue = _commandQueues.GetOrAdd(serialNumber, _ => new ConcurrentQueue<QueuedCommand>());
        queue.Enqueue(new QueuedCommand(command, downloadLogId));
    }

    public (string? Command, Guid? DownloadLogId) GetNextCommand(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return (null, null);

        if (_commandQueues.TryGetValue(serialNumber, out var queue))
        {
            if (queue.TryDequeue(out var cmd))
            {
                return (cmd.CommandText, cmd.DownloadLogId);
            }
        }

        return (null, null);
    }

    public bool HasPendingCommands(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return false;

        return _commandQueues.TryGetValue(serialNumber, out var queue) && !queue.IsEmpty;
    }

    public void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId)
    {
        _pendingExecutions.TryAdd(commandId, downloadLogId);
    }

    public Guid? GetAndRemovePendingExecution(string commandId)
    {
        if (_pendingExecutions.TryRemove(commandId, out var logId))
        {
            return logId;
        }
        return null;
    }

    public async Task<DeviceInfoDto?> WaitForDeviceInfoAsync(string serialNumber, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;

        var tcs = new TaskCompletionSource<DeviceInfoDto?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _deviceInfoWaiters.AddOrUpdate(serialNumber, tcs, (_, oldTcs) =>
        {
            oldTcs.TrySetResult(null);
            return tcs;
        });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            using (cts.Token.Register(() => tcs.TrySetResult(null)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _deviceInfoWaiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<DeviceInfoDto?>>(serialNumber, tcs));
        }
    }

    public void NotifyDeviceInfoReceived(string serialNumber, DeviceInfoDto deviceInfo)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return;

        if (_deviceInfoWaiters.TryGetValue(serialNumber, out var tcs))
        {
            tcs.TrySetResult(deviceInfo);
        }
    }
}
