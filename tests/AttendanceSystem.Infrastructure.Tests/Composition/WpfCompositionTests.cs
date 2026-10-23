using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DependencyInjection;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Attendance.EventHandlers;
using AttendanceSystem.Domain.Events;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Infrastructure.DependencyInjection;
using AttendanceSystem.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Composition;

public class WpfCompositionTests
{
    private ServiceProvider CreateWpfServiceProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AttendanceDb", "Host=localhost;Database=test;Username=postgres;Password=pass" },
                { "ZKTecoService:ApiKey", "test-key" }
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        // Registrar servicios compartidos de Aplicación e Infraestructura (H13)
        services.AddApplicationServices();
        services.AddInfrastructureServices(config);

        // Servicios específicos de WPF
        services.AddSingleton<ICurrentUserService>(new MockCurrentUserService());
        services.AddSingleton<IAttendanceJobScheduler, MockJobScheduler>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [Fact]
    public void WpfContainer_ShouldResolveAllPreviouslyMissingDependencies_H13()
    {
        using var sp = CreateWpfServiceProvider();
        using var scope = sp.CreateScope();

        // 1. Clientes y fábricas de dispositivos
        scope.ServiceProvider.GetService<IDeviceClientFactory>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IDeviceLockService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IAdmsCommandService>().Should().NotBeNull();

        // F03-02: WPF comparte el protocolo de exclusión de Blazor (bloqueo PostgreSQL por transacción).
        scope.ServiceProvider.GetService<IAttendanceCalculationLockService>().Should().BeOfType<AttendanceSystem.Infrastructure.Services.PostgresAttendanceCalculationLockService>();
        scope.ServiceProvider.GetService<IPersistenceFailureClassifier>().Should().BeOfType<AttendanceSystem.Infrastructure.Services.NpgsqlPersistenceFailureClassifier>();

        // 2. Servicios de transferencia y sincronización
        scope.ServiceProvider.GetService<ILogTransferService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ICloudLogSyncService>().Should().NotBeNull();

        // 3. Repositorios previamente faltantes
        scope.ServiceProvider.GetService<IExternalAttendanceLogRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ISystemAlertRepository>().Should().NotBeNull();

        // 4. Servicios de dominio y validación
        scope.ServiceProvider.GetService<AttendanceDeduplicationService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<AttendanceValidationService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IEmailService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IRestoreStateService>().Should().NotBeNull();
    }

    [Fact]
    public void WpfContainer_ShouldResolveDownloadFromDeviceCommandHandler_WithinScope_H13()
    {
        using var sp = CreateWpfServiceProvider();
        using var scope = sp.CreateScope();

        // El handler de descarga fallaba en WPF debido a dependencias ausentes
        var handler = scope.ServiceProvider.GetService<IRequestHandler<DownloadFromDeviceCommand, Application.Common.Result<Application.DTOs.DownloadResultDto>>>();

        handler.Should().NotBeNull();
    }

    [Fact]
    public void WpfContainer_ShouldResolveAttendanceRecordedEventHandler_WithinScope_H13()
    {
        using var sp = CreateWpfServiceProvider();
        using var scope = sp.CreateScope();

        // Requería IEmailService que estaba ausente en WPF
        var handlers = scope.ServiceProvider.GetServices<INotificationHandler<AttendanceRecordedEvent>>();

        handlers.Should().NotBeEmpty();
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-user-id";
        public string? UserName => "TestUser";
        public bool IsAuthenticated => true;
    }

    private class MockJobScheduler : IAttendanceJobScheduler
    {
        public void ScheduleAutoDownload(TimeSpan timeOfDay) { }
        public void DisableAutoDownload() { }
        public void ScheduleCriticalAbsenceCheck() { }
        public void ScheduleAutoBackup(TimeSpan timeOfDay) { }
        public void DisableAutoBackup() { }
        public void ScheduleAutoReport(TimeSpan timeOfDay) { }
        public void DisableAutoReport() { }
        public void ScheduleDeviceHeartbeat() { }
        public void ScheduleCloudSync() { }
        public void ScheduleScheduleIntegrityCheck() { }
        public void EnqueueBiometricSync(string deviceId, string employeeId) { }
        public void EnqueueAttendanceProcessing(DateTime startDate, DateTime endDate, string? employeeId = null) { }
    }
}
