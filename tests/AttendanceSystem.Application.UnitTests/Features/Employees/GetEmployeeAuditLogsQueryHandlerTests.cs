namespace AttendanceSystem.Application.UnitTests.Features.Employees;

using System.Text.Json;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class GetEmployeeAuditLogsQueryHandlerTests
{
    private readonly Mock<IEmployeeAuditLogRepository> _repoMock;
    private readonly Mock<ILogger<GetEmployeeAuditLogsQueryHandler>> _loggerMock;
    private readonly GetEmployeeAuditLogsQueryHandler _handler;

    public GetEmployeeAuditLogsQueryHandlerTests()
    {
        _repoMock = new Mock<IEmployeeAuditLogRepository>();
        _loggerMock = new Mock<ILogger<GetEmployeeAuditLogsQueryHandler>>();
        _handler = new GetEmployeeAuditLogsQueryHandler(_repoMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenLogsExist_ReturnsLogsOrderedAndDeserialized()
    {
        // Arrange
        var employeeId = EmployeeId.From("EMP001");
        var changes = new List<EmployeeAuditFieldChangeDto>
        {
            new() { PropertyName = "BranchId", DisplayName = "Sucursal", OldValue = "Matriz", NewValue = "Sucursal Norte" }
        };
        var changesJson = JsonSerializer.Serialize(changes);

        var log1 = EmployeeAuditLog.Create(
            employeeId,
            "Alta de Empleado",
            DateTime.UtcNow.AddDays(-5),
            "user1",
            "admin",
            "Alta inicial");

        var log2 = EmployeeAuditLog.Create(
            employeeId,
            "Modificación de Perfil",
            DateTime.UtcNow.AddDays(-1),
            "user2",
            "supervisor",
            "Cambio de sucursal",
            changesJson);

        _repoMock.Setup(r => r.GetByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeAuditLog> { log1, log2 });

        var query = new GetEmployeeAuditLogsQuery("EMP001");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // Most recent first
        result.Value[0].Action.Should().Be("Modificación de Perfil");
        result.Value[0].UserName.Should().Be("supervisor");
        result.Value[0].Changes.Should().HaveCount(1);
        result.Value[0].Changes[0].DisplayName.Should().Be("Sucursal");
        result.Value[0].Changes[0].NewValue.Should().Be("Sucursal Norte");

        result.Value[1].Action.Should().Be("Alta de Empleado");
        result.Value[1].UserName.Should().Be("admin");
    }

    [Fact]
    public async Task Handle_WhenNoLogs_ReturnsEmptyList()
    {
        // Arrange
        var employeeId = EmployeeId.From("EMP999");
        _repoMock.Setup(r => r.GetByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeAuditLog>());

        var query = new GetEmployeeAuditLogsQuery("EMP999");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
