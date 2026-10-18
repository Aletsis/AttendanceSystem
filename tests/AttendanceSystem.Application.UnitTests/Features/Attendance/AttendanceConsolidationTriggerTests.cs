using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.EventHandlers;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Events;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class AttendanceConsolidationTriggerTests
{
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IAttendanceConsolidationRepository> _consolidationRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAttendanceJobScheduler> _jobSchedulerMock = new();
    private readonly Mock<ILogger<AttendanceRecordedEventHandler>> _loggerMock = new();

    private readonly AttendanceRecordedEventHandler _handler;
    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-CONS-1");

    public AttendanceConsolidationTriggerTests()
    {
        _handler = new AttendanceRecordedEventHandler(
            _emailServiceMock.Object,
            _consolidationRepoMock.Object,
            _jobSchedulerMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenAttendanceRecordedDispatched_ShouldPersistConsolidationRequestAndEnqueueJob()
    {
        // Arrange (H27)
        var punchTime = new DateTime(2026, 10, 15, 8, 30, 0);
        var recordId = AttendanceRecordId.CreateNew();
        var domainEvent = new AttendanceRecordedEvent(recordId, _employeeId, punchTime, CheckType.CheckIn);

        _consolidationRepoMock.Setup(r => r.GetPendingByEmployeeAsync(_employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceConsolidationRequest?)null);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert: 1. Se agrega una nueva solicitud durable de consolidación
        _consolidationRepoMock.Verify(r => r.AddAsync(
            It.Is<AttendanceConsolidationRequest>(req =>
                req.EmployeeId == _employeeId &&
                req.StartDate == punchTime.Date.AddDays(-1) &&
                req.EndDate == punchTime.Date &&
                req.Status == ConsolidationStatus.Pending),
            It.IsAny<CancellationToken>()), Times.Once);

        // 2. Se confirman los cambios
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // 3. Se encola el recálculo en el planificador de trabajos (H27)
        _jobSchedulerMock.Verify(j => j.EnqueueAttendanceProcessing(
            punchTime.Date.AddDays(-1),
            punchTime.Date,
            _employeeId.Value), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPendingRequestAlreadyExists_ShouldExpandDateRange()
    {
        // Arrange (H27)
        // Ya existía una solicitud pendiente del día 12
        var existingRequest = AttendanceConsolidationRequest.Create(
            _employeeId,
            new DateTime(2026, 10, 11),
            new DateTime(2026, 10, 12));

        _consolidationRepoMock.Setup(r => r.GetPendingByEmployeeAsync(_employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRequest);

        // Llega un nuevo marcaje el día 15
        var punchTime = new DateTime(2026, 10, 15, 17, 0, 0);
        var recordId = AttendanceRecordId.CreateNew();
        var domainEvent = new AttendanceRecordedEvent(recordId, _employeeId, punchTime, CheckType.CheckOut);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert: La solicitud existente expande su rango para cubrir hasta el nuevo marcaje
        existingRequest.StartDate.Should().Be(new DateTime(2026, 10, 11));
        existingRequest.EndDate.Should().Be(new DateTime(2026, 10, 15));

        _consolidationRepoMock.Verify(r => r.Update(existingRequest), Times.Once);
        _consolidationRepoMock.Verify(r => r.AddAsync(It.IsAny<AttendanceConsolidationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
