using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Blazor.Server.Controllers;

[ApiController]
[Route("api/external-logs")]
public class ExternalLogsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<ExternalLogsController> _logger;

    public ExternalLogsController(ISender sender, ILogger<ExternalLogsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveLog([FromBody] ExternalLogRequest request)
    {
        _logger.LogInformation("Recibiendo log externo para empleado {EmployeeId}", request.EmployeeId);

        // Mapear a RecordAttendanceCommand
        // Usamos "EXTERNAL_API" como DeviceId para identificar la procedencia
        var command = new RecordAttendanceCommand(
            request.EmployeeId,
            "EXTERNAL_API",
            request.CheckTime,
            request.VerifyMethod,
            request.CheckType
        );

        var result = await _sender.Send(command);

        if (result.IsSuccess)
        {
            return Ok(new { Success = true, Message = "Log registrado correctamente", RecordId = result.Value });
        }

        _logger.LogWarning("Error al registrar log externo: {Error}", result.Error);
        return BadRequest(new { Success = false, Error = result.Error });
    }
}

public class ExternalLogRequest
{
    public string EmployeeId { get; set; } = null!;
    public DateTime CheckTime { get; set; }
    public int VerifyMethod { get; set; }
    public int CheckType { get; set; }
}
