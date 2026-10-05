using AttendanceSystem.Application.Features.ExternalEmployees;
using AttendanceSystem.Application.Features.ExternalEmployees.Commands;
using AttendanceSystem.Application.Features.ExternalEmployees.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Blazor.Server.Controllers;

[ApiController]
[Route("api/external-employees")]
public class ExternalEmployeesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<ExternalEmployeesController> _logger;

    public ExternalEmployeesController(ISender sender, ILogger<ExternalEmployeesController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? branchId, [FromQuery] string? search)
    {
        var result = await _sender.Send(new GetExternalEmployeesQuery(branchId, search));
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BadRequest(new { Success = false, Error = result.Error });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _sender.Send(new GetExternalEmployeeByIdQuery(id));
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return NotFound(new { Success = false, Error = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExternalEmployeeCommand command)
    {
        var result = await _sender.Send(command);
        if (result.IsSuccess)
        {
            return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
        }

        return BadRequest(new { Success = false, Error = result.Error });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExternalEmployeeRequest request)
    {
        var command = new UpdateExternalEmployeeCommand(
            id,
            request.BranchId,
            request.EmployeeNumber,
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            request.Position,
            request.Department,
            request.Status,
            request.CardNumber);

        var result = await _sender.Send(command);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BadRequest(new { Success = false, Error = result.Error });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _sender.Send(new DeleteExternalEmployeeCommand(id));
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return BadRequest(new { Success = false, Error = result.Error });
    }
}

public class UpdateExternalEmployeeRequest
{
    public string BranchId { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public Domain.Enumerations.EmployeeStatus Status { get; set; }
    public string? CardNumber { get; set; }
}
