using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Blazor.Server.Controllers;

[ApiController]
[Route("api/external-logs")]
public class ExternalLogsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IExternalAttendanceLogRepository _externalLogRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExternalLogsController> _logger;

    public ExternalLogsController(
        ISender sender,
        IExternalAttendanceLogRepository externalLogRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        ILogger<ExternalLogsController> logger)
    {
        _sender = sender;
        _externalLogRepository = externalLogRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveLog([FromBody] ExternalLogRequest request)
    {
        _logger.LogInformation("Recibiendo log para empleado {EmployeeId} (Sucursal: {BranchCode})", 
            request.EmployeeId, request.BranchCode ?? "N/A");

        // Si se provee código de sucursal y corresponde a una sucursal externa, almacenar en ExternalAttendanceLogs
        if (!string.IsNullOrWhiteSpace(request.BranchCode))
        {
            var branches = await _branchRepository.GetAllAsync();
            var branch = branches.FirstOrDefault(b => b.Code.Equals(request.BranchCode.Trim(), StringComparison.OrdinalIgnoreCase));
            
            if (branch != null && branch.IsExternal)
            {
                var externalLog = ExternalAttendanceLog.Create(
                    branch.Code,
                    request.EmployeeId.Trim(),
                    request.CheckTime,
                    request.VerifyMethod,
                    request.CheckType,
                    request.SourceDevice ?? "EXTERNAL_API"
                );

                await _externalLogRepository.AddAsync(externalLog);
                await _unitOfWork.SaveChangesAsync();

                return Ok(new { Success = true, Message = "Log registrado en registros de sucursal externa", LogId = externalLog.Id });
            }
        }

        // Si no es sucursal externa o no se especificó código, procesar como marcaje estándar
        var command = new RecordAttendanceCommand(
            request.EmployeeId,
            request.SourceDevice ?? "EXTERNAL_API",
            request.CheckTime,
            request.VerifyMethod,
            request.CheckType
        );

        var result = await _sender.Send(command);

        if (result.IsSuccess)
        {
            return Ok(new { Success = true, Message = "Log registrado correctamente", RecordId = result.Value });
        }

        _logger.LogWarning("Error al registrar log: {Error}", result.Error);
        return BadRequest(new { Success = false, Error = result.Error });
    }
}

public class ExternalLogRequest
{
    public string? BranchCode { get; set; }
    public string EmployeeId { get; set; } = null!;
    public DateTime CheckTime { get; set; }
    public int VerifyMethod { get; set; }
    public int CheckType { get; set; }
    public string? SourceDevice { get; set; }
}
