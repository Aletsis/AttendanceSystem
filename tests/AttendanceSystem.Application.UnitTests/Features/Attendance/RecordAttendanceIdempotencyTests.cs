using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class RecordAttendanceIdempotencyTests
{
    private readonly Mock<IAttendanceRepository> _repoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ITimeNormalizer> _timeNormalizerMock = new();

    public RecordAttendanceIdempotencyTests()
    {
        _timeNormalizerMock.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        _timeNormalizerMock.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, tz) => DateOnly.FromDateTime(dt));
    }

    [Fact]
    public async Task RecordAttendanceCommandHandler_WhenDuplicateSubmitted_ShouldReturnExistingIdWithoutInserting()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var existingRecord = AttendanceRecord.Create(
            EmployeeId.From("EMP-001"),
            DeviceId.From("DEV-01"),
            new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc),
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        // F03-01: la existencia se consulta por identidad (empleado, instante), no por dispositivo.
        _repoMock.Setup(r => r.GetByIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(EmployeeId, DateTime)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { existingRecord });

        var handler = new RecordAttendanceCommandHandler(
            _repoMock.Object,
            _uowMock.Object,
            _timeNormalizerMock.Object,
            NullLogger<RecordAttendanceCommandHandler>.Instance);

        var command = new RecordAttendanceCommand(
            "EMP-001",
            "DEV-01",
            new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc),
            1,
            0);

        // Act
        var result = await handler.Handle(command, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existingRecord.Id.Value);

        // No se debe intentar agregar a la base de datos de nuevo
        _repoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<AttendanceRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        _uowMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task RecordAttendanceBatchCommandHandler_ShouldDeduplicateInternalAndExistingRecords()
    {
        // Arrange
        var checkTime1 = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
        var checkTime2 = new DateTime(2026, 10, 17, 8, 0, 15, DateTimeKind.Utc);

        var existingRecord = AttendanceRecord.Create(
            EmployeeId.From("EMP-001"),
            DeviceId.From("DEV-01"),
            checkTime1,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        // El registro checkTime1 ya existe en la BD
        _repoMock.Setup(r => r.GetByIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(EmployeeId, DateTime)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { existingRecord });

        var handler = new RecordAttendanceBatchCommandHandler(
            _repoMock.Object,
            _uowMock.Object,
            _timeNormalizerMock.Object,
            NullLogger<RecordAttendanceBatchCommandHandler>.Instance);

        // El lote contiene:
        // 1. checkTime1 (ya existe en BD)
        // 2. checkTime2 (nuevo)
        // 3. checkTime2 repetido en el mismo lote
        var batchCommand = new RecordAttendanceBatchCommand(
            "DEV-01",
            new List<AttendanceLogDto>
            {
                new("EMP-001", checkTime1, 1, 0),
                new("EMP-001", checkTime2, 1, 0),
                new("EMP-001", checkTime2, 1, 0)
            });

        // Act
        var result = await handler.Handle(batchCommand, default);

        // Assert: Solo debe insertarse 1 registro (checkTime2)
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        _repoMock.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<AttendanceRecord>>(records => records.Count() == 1),
            default), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
