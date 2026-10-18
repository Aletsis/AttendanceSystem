using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class AttendanceCalculationLockServiceTests
{
    [Fact]
    public async Task LockService_ConcurrentRecalculations_SerializesOrderedByEmployeeIdWithoutDeadlock()
    {
        // Arrange (H06 deadlock prevention test)
        var lockService = new AttendanceCalculationLockService();
        var emp1 = EmployeeId.From("EMP-001");
        var emp2 = EmployeeId.From("EMP-002");

        var executionOrder = new List<string>();

        // Tarea A pide locks para {emp2, emp1}
        var taskA = Task.Run(async () =>
        {
            await lockService.ExecuteWithLockAsync(new[] { emp2, emp1 }, async () =>
            {
                executionOrder.Add("Start A");
                await Task.Delay(50);
                executionOrder.Add("End A");
            });
        });

        // Tarea B pide locks para {emp1, emp2} simultáneamente en orden inverso
        var taskB = Task.Run(async () =>
        {
            await Task.Delay(10); // Ligeramente desfasado
            await lockService.ExecuteWithLockAsync(new[] { emp1, emp2 }, async () =>
            {
                executionOrder.Add("Start B");
                await Task.Delay(20);
                executionOrder.Add("End B");
            });
        });

        await Task.WhenAll(taskA, taskB);

        // Assert: Ambas tareas se ejecutan secuencialmente sin deadlock
        executionOrder.Should().HaveCount(4);
        var startAIndex = executionOrder.IndexOf("Start A");
        var endAIndex = executionOrder.IndexOf("End A");
        var startBIndex = executionOrder.IndexOf("Start B");
        var endBIndex = executionOrder.IndexOf("End B");

        // Una tarea termina completamente antes de que la otra inicie
        (endAIndex < startBIndex || endBIndex < startAIndex).Should().BeTrue();
    }
}
