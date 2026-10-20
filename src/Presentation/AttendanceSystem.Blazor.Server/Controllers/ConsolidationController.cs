using AttendanceSystem.Application.Features.Attendance.Consolidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Blazor.Server.Controllers;

/// <summary>
/// Operación de la consolidación de asistencia: estado de solicitudes y eventos (antigüedad, intentos,
/// fallos terminales) y reintento tras corregir la causa.
/// </summary>
[ApiController]
[Route("api/consolidation")]
[Authorize(Roles = "Administrador")]
public class ConsolidationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ConsolidationController> _logger;

    public ConsolidationController(IMediator mediator, ILogger<ConsolidationController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ConsolidationStatusDto>> GetStatus([FromQuery] int recentFailures = 20)
    {
        return Ok(await _mediator.Send(new GetConsolidationStatusQuery(Math.Clamp(recentFailures, 1, 200)), HttpContext.RequestAborted));
    }

    /// <summary>Reactiva solicitudes fallidas: todas, o una si se indica su identificador.</summary>
    [HttpPost("requests/retry")]
    public async Task<IActionResult> RetryFailedRequests([FromQuery] Guid? requestId = null)
    {
        var result = await _mediator.Send(new RetryFailedConsolidationsCommand(requestId), HttpContext.RequestAborted);
        _logger.LogInformation("Reintento operativo de consolidación: {Count} solicitudes reactivadas por {User}.", result.Value, User.Identity?.Name);
        return Ok(new { Reactivated = result.Value });
    }

    /// <summary>Reactiva eventos muertos del outbox: todos, o uno si se indica su identificador.</summary>
    [HttpPost("events/retry")]
    public async Task<IActionResult> RetryDeadLetteredEvents([FromQuery] Guid? messageId = null)
    {
        var result = await _mediator.Send(new RetryDeadLetteredEventsCommand(messageId), HttpContext.RequestAborted);
        _logger.LogInformation("Reintento operativo de outbox: {Count} eventos reactivados por {User}.", result.Value, User.Identity?.Name);
        return Ok(new { Reactivated = result.Value });
    }
}
