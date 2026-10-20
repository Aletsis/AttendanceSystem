using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class RegisterManualAttendanceCommandHandlerTests
{
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ITimeNormalizer> _timeNormalizerMock;
    private readonly Mock<ISender> _senderMock = new();
    private readonly List<AttendanceRecord> _added = new();
    private readonly RegisterManualAttendanceCommandHandler _handler;

    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-001");
    private readonly DateTime _date = new(2026, 9, 17, 8, 0, 0);

    public RegisterManualAttendanceCommandHandlerTests()
    {
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _timeNormalizerMock = new Mock<ITimeNormalizer>();

        _timeNormalizerMock.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        _timeNormalizerMock.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateOnly.FromDateTime(dt));

        // La ingesta agrega con AddRangeAsync; el registro queda disponible por Id para la asignación manual.
        _attendanceRepoMock
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AttendanceRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AttendanceRecord>, CancellationToken>((records, _) => _added.AddRange(records))
            .Returns(Task.CompletedTask);
        _attendanceRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<AttendanceRecordId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecordId id, CancellationToken _) => _added.FirstOrDefault(r => r.Id == id));
        _attendanceRepoMock
            .Setup(r => r.GetByIdentitiesAsync(It.IsAny<IReadOnlyCollection<(EmployeeId, DateTime)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        // F03-03: la asignación manual real (con su protocolo de bloqueo) procesa el marcaje capturado.
        var manualAssign = new ManuallyAssignAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            Mock.Of<IAttendanceAdjustmentRepository>(),
            Mock.Of<IPunchAssignmentRepository>(),
            _unitOfWorkMock.Object,
            Mock.Of<IAttendanceCalculationLockService>());
        _senderMock
            .Setup(s => s.Send(It.IsAny<ManuallyAssignAttendanceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ManuallyAssignAttendanceCommand, CancellationToken>((c, ct) => manualAssign.Handle(c, ct));

        _handler = new RegisterManualAttendanceCommandHandler(
            _attendanceRepoMock.Object,
            _dailyRepoMock.Object,
            _unitOfWorkMock.Object,
            _timeNormalizerMock.Object,
            _senderMock.Object);
    }

    [Fact]
    public async Task Handle_WhenNoDailyAttendanceExists_ShouldCreateRecordAndCommit()
    {
        // Arrange
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: _date,
            Type: "Entrada");

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyAttendance?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _added.Should().ContainSingle(rec =>
            rec.EmployeeId == _employeeId &&
            rec.CheckType == CheckType.CheckIn &&
            rec.VerifyMethod == VerifyMethod.Manual);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _senderMock.Verify(s => s.Send(It.IsAny<ManuallyAssignAttendanceCommand>(), It.IsAny<CancellationToken>()), Times.Never,
            "sin jornada calculada el recálculo posterior considerará el marcaje");
    }

    [Fact]
    public async Task Handle_WhenCheckInAlreadyExists_ShouldUnassignPreviousAndAssignNewRecordAndRecalculate()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var oldCheckInRecord = AttendanceRecord.Create(
            _employeeId,
            DeviceId.From("DEV-01"),
            _date,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);
        oldCheckInRecord.MarkAsProcessed();

        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: null,
            isRestDay: false,
            checkInRecordId: oldCheckInRecord.Id);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        _added.Add(oldCheckInRecord);

        var newCheckInTime = _date.AddMinutes(20); // 8:20 AM (tarde con 10 min de tolerancia = 20 min de retardo)
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: newCheckInTime,
            Type: "Entrada");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        oldCheckInRecord.Status.Should().Be(AttendanceStatus.Pending); // Desasignado / vuelto a pendiente
        _attendanceRepoMock.Verify(r => r.UpdateAsync(oldCheckInRecord, It.IsAny<CancellationToken>()), Times.Once);

        _added.Should().ContainSingle(rec =>
            rec.EmployeeId == _employeeId &&
            rec.CheckTime == newCheckInTime &&
            rec.CheckType == CheckType.CheckIn &&
            rec.Status == AttendanceStatus.Processed);

        existingDaily.ActualCheckIn.Should().Be(newCheckInTime);
        existingDaily.LateMinutes.Should().Be(20); // Recalculado
        // Un guardado de la ingesta y otro de la asignación manual coordinada.
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_WhenCheckOutAlreadyExists_ShouldUnassignPreviousAndAssignNewRecordAndRecalculate()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var oldCheckOutRecord = AttendanceRecord.Create(
            _employeeId,
            DeviceId.From("DEV-01"),
            _date.AddHours(8),
            VerifyMethod.Fingerprint,
            CheckType.CheckOut);
        oldCheckOutRecord.MarkAsProcessed();

        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: _date.AddHours(8),
            isRestDay: false,
            checkOutRecordId: oldCheckOutRecord.Id);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        _added.Add(oldCheckOutRecord);

        var newCheckOutTime = _date.AddHours(9); // 1 hora de tiempo extra
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: newCheckOutTime,
            Type: "Salida");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        oldCheckOutRecord.Status.Should().Be(AttendanceStatus.Pending); // Desasignado
        _attendanceRepoMock.Verify(r => r.UpdateAsync(oldCheckOutRecord, It.IsAny<CancellationToken>()), Times.Once);

        _added.Should().ContainSingle(rec =>
            rec.EmployeeId == _employeeId &&
            rec.CheckTime == newCheckOutTime &&
            rec.CheckType == CheckType.CheckOut &&
            rec.Status == AttendanceStatus.Processed);

        existingDaily.ActualCheckOut.Should().Be(newCheckOutTime);
        existingDaily.EarlyDepartureMinutes.Should().Be(0);
        existingDaily.MissingCheckOut.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_WhenValidCheckOutForExistingCheckIn_ShouldUpdateDailyAttendance()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: null,
            isRestDay: false);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        var checkOutTime = _date.AddHours(8);
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: checkOutTime,
            Type: "Salida");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingDaily.ActualCheckOut.Should().Be(checkOutTime);
        existingDaily.MissingCheckOut.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
