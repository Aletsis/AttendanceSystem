using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Automation;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Automation;

/// <summary>Auditoría de tareas, bloque G: una alerta por causa mientras siga abierta, registrada antes de avisar.</summary>
public class OperationalAlertsTests
{
    private readonly List<SystemAlert> _alerts = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly OperationalAlerts _service;

    public OperationalAlertsTests()
    {
        var repository = new Mock<ISystemAlertRepository>();
        repository.Setup(r => r.GetActiveAlertsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _alerts.Where(a => !a.IsResolved).ToList());
        repository.Setup(r => r.AddAsync(It.IsAny<SystemAlert>(), It.IsAny<CancellationToken>()))
            .Callback<SystemAlert, CancellationToken>((a, _) => _alerts.Add(a)).Returns(Task.CompletedTask);
        _service = new OperationalAlerts(repository.Object, Mock.Of<IUnitOfWork>(), _email.Object, NullLogger<OperationalAlerts>.Instance);
    }

    [Fact]
    public async Task AnOpenAlert_IsNotRepeated_ButItsTextIsUpdated()
    {
        (await _service.RaiseAsync("relojes:20261014", "Relojes", "R1 caído")).Should().BeTrue();
        (await _service.RaiseAsync("relojes:20261014", "Relojes", "R1 y R2 caídos")).Should().BeFalse();

        _alerts.Should().ContainSingle().Which.Message.Should().Be("R1 y R2 caídos");
        _email.Verify(e => e.SendAlertAsync("Relojes", It.IsAny<string>(), AlertLevel.SystemFailure, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_ClosesOnlyTheMatchingAlerts_AndTheCauseCanAlertAgain()
    {
        await _service.RaiseAsync("tarea:automated-database-backup:20261013", "Respaldo", "falló");
        await _service.RaiseAsync("tarea:automated-daily-report:20261013", "Reporte", "falló");

        await _service.ResolveAsync("tarea:automated-database-backup:");

        _alerts.Single(a => a.ReferenceId!.Contains("backup")).IsResolved.Should().BeTrue();
        _alerts.Single(a => a.ReferenceId!.Contains("report")).IsResolved.Should().BeFalse();
        (await _service.RaiseAsync("tarea:automated-database-backup:20261013", "Respaldo", "falló otra vez")).Should().BeTrue();
    }

    [Fact]
    public async Task AFailedEmail_KeepsTheAlertRecorded()
    {
        _email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP caído"));

        (await _service.RaiseAsync("cierre:20260810", "Cierre", "pospuesto")).Should().BeTrue();

        _alerts.Should().ContainSingle();
    }

    [Fact]
    public async Task ALongMessage_FitsTheColumn_AndTheEmailCarriesItWhole()
    {
        var message = new string('x', 800);
        string? body = null;
        _email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, AlertLevel, CancellationToken>((_, b, _, _) => body = b).Returns(Task.CompletedTask);

        await _service.RaiseAsync("relojes:20261014", "Relojes", message);

        _alerts.Single().Message.Length.Should().Be(500);
        body.Should().Contain(message);
    }
}
