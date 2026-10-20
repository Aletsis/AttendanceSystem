using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class SetDailyRestDayOverrideCommandHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _repositoryMock;
    private readonly Mock<IAttendanceAdjustmentRepository> _adjustmentRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly SetDailyRestDayOverrideCommandHandler _handler;

    public SetDailyRestDayOverrideCommandHandlerTests()
    {
        _repositoryMock = new Mock<IDailyAttendanceRepository>();
        _adjustmentRepoMock = new Mock<IAttendanceAdjustmentRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _handler = new SetDailyRestDayOverrideCommandHandler(
            _repositoryMock.Object,
            _adjustmentRepoMock.Object,
            _unitOfWorkMock.Object, Mock.Of<IAttendanceCalculationLockService>());
    }

    [Fact]
    public async Task Handle_WhenAttendanceNotFound_ShouldReturnFalse()
    {
        // Arrange
        var command = new SetDailyRestDayOverrideCommand(Guid.NewGuid(), IsRestDay: true);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<DailyAttendanceId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyAttendance?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAttendanceFound_ShouldUpdateRestDayAndReturnTrue()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var attendance = DailyAttendance.Create(
            employeeId: EmployeeId.From("EMP-001"),
            date: new DateTime(2026, 9, 17),
            shift: shift,
            checkIn: null,
            checkOut: null,
            isRestDay: false);

        var command = new SetDailyRestDayOverrideCommand(attendance.Id.Value, IsRestDay: true);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(attendance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        // F03-03: bajo el bloqueo la jornada se relee por empleado y fecha.
        _repositoryMock
            .Setup(r => r.GetByEmployeeAndDateAsync(attendance.EmployeeId, attendance.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        attendance.IsRestDay.Should().BeTrue();
        _repositoryMock.Verify(r => r.Update(attendance), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAttendanceWasReplacedByRecalculation_ReturnsFalseAndRollsBack()
    {
        // La pantalla muestra un Id que un recálculo concurrente ya reemplazó (F02-06 / F03-03).
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var stale = DailyAttendance.Create(EmployeeId.From("EMP-001"), new DateTime(2026, 9, 17), shift, null, null, false);
        var current = DailyAttendance.Create(EmployeeId.From("EMP-001"), new DateTime(2026, 9, 17), shift, null, null, false);

        _repositoryMock.Setup(r => r.GetByIdAsync(stale.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        _repositoryMock.Setup(r => r.GetByEmployeeAndDateAsync(stale.EmployeeId, stale.Date, It.IsAny<CancellationToken>())).ReturnsAsync(current);

        var result = await _handler.Handle(new SetDailyRestDayOverrideCommand(stale.Id.Value, IsRestDay: true), CancellationToken.None);

        result.Should().BeFalse();
        current.IsRestDay.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
