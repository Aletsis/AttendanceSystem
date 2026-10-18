using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Services;

public class AttendanceCalculationLockService : IAttendanceCalculationLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task ExecuteWithLockAsync(
        IEnumerable<EmployeeId> employeeIds,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        var sortedIds = employeeIds
            .Select(e => e.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var acquiredSemaphores = new List<SemaphoreSlim>(sortedIds.Count);

        try
        {
            foreach (var id in sortedIds)
            {
                var semaphore = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync(cancellationToken);
                acquiredSemaphores.Add(semaphore);
            }

            await action();
        }
        finally
        {
            // Liberar en orden inverso a la adquisición
            for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
            {
                acquiredSemaphores[i].Release();
            }
        }
    }

    public async Task<T> ExecuteWithLockAsync<T>(
        IEnumerable<EmployeeId> employeeIds,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        T result = default!;
        await ExecuteWithLockAsync(employeeIds, async () =>
        {
            result = await action();
        }, cancellationToken);
        return result;
    }
}
