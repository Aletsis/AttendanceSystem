namespace AttendanceSystem.Application.Features.Employees.Queries;

using System.Text.Json;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed record GetEmployeeAuditLogsQuery(string EmployeeId) : IRequest<Result<IReadOnlyList<EmployeeAuditLogDto>>>;

public sealed class GetEmployeeAuditLogsQueryHandler : IRequestHandler<GetEmployeeAuditLogsQuery, Result<IReadOnlyList<EmployeeAuditLogDto>>>
{
    private readonly IEmployeeAuditLogRepository _auditLogRepository;
    private readonly ILogger<GetEmployeeAuditLogsQueryHandler> _logger;

    public GetEmployeeAuditLogsQueryHandler(
        IEmployeeAuditLogRepository auditLogRepository,
        ILogger<GetEmployeeAuditLogsQueryHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeAuditLogDto>>> Handle(
        GetEmployeeAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = EmployeeId.From(request.EmployeeId);
            var logs = await _auditLogRepository.GetByEmployeeIdAsync(employeeId, cancellationToken);

            var dtos = logs
                .OrderByDescending(l => l.Timestamp)
                .Select(l =>
                {
                    List<EmployeeAuditFieldChangeDto> changes = new();
                    if (!string.IsNullOrWhiteSpace(l.ChangesJson))
                    {
                        try
                        {
                            changes = JsonSerializer.Deserialize<List<EmployeeAuditFieldChangeDto>>(l.ChangesJson) ?? new();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error al deserializar cambios de auditoría {AuditLogId}", l.Id);
                        }
                    }

                    return new EmployeeAuditLogDto
                    {
                        Id = l.Id,
                        EmployeeId = l.EmployeeId.Value,
                        Action = l.Action,
                        Timestamp = l.Timestamp,
                        UserId = l.UserId,
                        UserName = l.UserName ?? "Sistema",
                        Details = l.Details,
                        Changes = changes
                    };
                })
                .ToList();

            return Result<IReadOnlyList<EmployeeAuditLogDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener bitácora de auditoría para el empleado {EmployeeId}", request.EmployeeId);
            return Result<IReadOnlyList<EmployeeAuditLogDto>>.Failure("Error al consultar la bitácora del empleado");
        }
    }
}
