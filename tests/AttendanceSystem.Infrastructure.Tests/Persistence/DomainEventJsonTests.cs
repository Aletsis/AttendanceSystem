using System;
using System.Text.Json;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Events;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence.Outbox;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Los eventos guardados en el outbox deben poder leerse para reintentarlos. Antes, los objetos de valor
/// y las enumeraciones (constructores privados) hacían imposible deserializarlos.
/// </summary>
public class DomainEventJsonTests
{
    [Fact]
    public void AttendanceRecordedEvent_RoundTrips_WithValueObjectsAndEnumerations()
    {
        var original = new AttendanceRecordedEvent(AttendanceRecordId.CreateNew(), EmployeeId.From("EMP-JSON-1"),
            new DateTime(2026, 5, 4, 14, 0, 0, DateTimeKind.Utc), CheckType.CheckOut, new DateOnly(2026, 5, 4));

        var json = DomainEventJson.Serialize(original);
        var restored = (AttendanceRecordedEvent)DomainEventJson.Deserialize(json, typeof(AttendanceRecordedEvent))!;

        restored.RecordId.Should().Be(original.RecordId);
        restored.EmployeeId.Should().Be(original.EmployeeId);
        restored.CheckType.Should().BeSameAs(CheckType.CheckOut);
        restored.WorkDate.Should().Be(original.WorkDate);
        restored.CheckTime.Should().Be(original.CheckTime);
    }

    [Fact]
    public void MessagesWrittenWithTheDefaultSerializer_RemainReadable()
    {
        // Formato que producía JsonSerializer sin opciones en los mensajes ya guardados.
        var original = new AttendanceRecordedEvent(AttendanceRecordId.CreateNew(), EmployeeId.From("EMP-JSON-2"),
            new DateTime(2026, 5, 4, 14, 0, 0, DateTimeKind.Utc), CheckType.CheckIn);
        var legacyJson = JsonSerializer.Serialize(original, original.GetType());

        var restored = (AttendanceRecordedEvent)DomainEventJson.Deserialize(legacyJson, typeof(AttendanceRecordedEvent))!;

        restored.RecordId.Should().Be(original.RecordId);
        restored.CheckType.Should().BeSameAs(CheckType.CheckIn);
        restored.WorkDate.Should().BeNull();
    }
}
