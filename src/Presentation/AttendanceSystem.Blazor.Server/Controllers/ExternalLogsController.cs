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
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly ILogger<ExternalLogsController> _logger;
    private readonly ITimeNormalizer _timeNormalizer;

    public ExternalLogsController(
        ISender sender,
        IExternalAttendanceLogRepository externalLogRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ITimeNormalizer timeNormalizer,
        ILogger<ExternalLogsController> logger)
    {
        _sender = sender;
        _externalLogRepository = externalLogRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _timeNormalizer = timeNormalizer;
        _logger = logger;
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveLog([FromBody] ExternalLogRequest request)
    {
        // H01: Validar autorización (Rol Administrador o cabecera x-api-key autorizada)
        var isAuthorized = User.IsInRole("Administrador");
        if (!isAuthorized)
        {
            var configuredApiKey = _configuration["ApiKey"] ?? _configuration["ZKTecoService:ApiKey"] ?? _configuration["ExternalLogs:ApiKey"];
            if (Request.Headers.TryGetValue("x-api-key", out var providedApiKey) &&
                !string.IsNullOrWhiteSpace(configuredApiKey) &&
                string.Equals(providedApiKey.ToString(), configuredApiKey, StringComparison.Ordinal))
            {
                isAuthorized = true;
            }
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Intento no autorizado de enviar log externo para empleado {EmployeeId}", request.EmployeeId);
            return Unauthorized(new { Success = false, Message = "Acceso no autorizado: Se requiere cabecera x-api-key válida o rol Administrador." });
        }

        _logger.LogInformation("Recibiendo log para empleado {EmployeeId} (Sucursal: {BranchCode})",
            request.EmployeeId, request.BranchCode ?? "N/A");

        var utcCheckTime = _timeNormalizer.ToUtc(request.CheckTime);

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
                    utcCheckTime,
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
            utcCheckTime,
            request.VerifyMethod,
            request.CheckType,
            Source: "ExternalApi"
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
