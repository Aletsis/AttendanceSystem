using Microsoft.AspNetCore.Mvc;
using MediatR;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance; 
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;
using AttendanceSystem.Domain.Repositories;
using System.Globalization;

namespace AttendanceSystem.Blazor.Server.Controllers;

[Route("iclock")]
[ApiController]
public class AdmsController : ControllerBase
{
    private readonly ILogger<AdmsController> _logger;
    private readonly IMediator _mediator;
    private readonly IDeviceRepository _deviceRepository;

    public AdmsController(
        ILogger<AdmsController> logger, 
        IMediator mediator,
        IDeviceRepository deviceRepository)
    {
        _logger = logger;
        _mediator = mediator;
        _deviceRepository = deviceRepository;
    }

    // Handshake / Check configuration
    [HttpGet("cdata")]
    public IActionResult CheckData([FromQuery] string SN)
    {
         _logger.LogInformation("ADMS Handshake received from {SerialNumber}", SN);
         return Ok("OK");
    }

    // Receive Data
    [HttpPost("cdata")]
    public async Task<IActionResult> ReceiveData([FromQuery] string SN, [FromQuery] string table)
    {
        // _logger.LogInformation("ADMS Data received from {SerialNumber}, Table={Table}", SN, table);
        
        if (table?.ToUpper() == "ATTLOG")
        {
            using var reader = new StreamReader(Request.Body);
            var content = await reader.ReadToEndAsync();
            
            if (!string.IsNullOrWhiteSpace(content))
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        // Expected format: USERID\tCHECKTIME\tVERIFYMODE\tINOUTMODE...
                        var parts = line.Split('\t');
                        if (parts.Length >= 2)
                        {
                            var userId = parts[0];
                            var timeStr = parts[1];
                            
                            if (DateTime.TryParse(timeStr, out var checkTime))
                            {
                                // We use a default command. Adjust parameters as needed.
                                // NOTE: We don't have the Device ID here immediately as Guid, we have SN.
                                // We might need to look up device by SN or pass SN.
                                // RecordAttendanceCommand expects DeviceId (Guid).
                                
                                // For now we just log to avoid complex lookup logic in this iteration 
                                // unless we want to do it right now.
                                // Ideally: look up Device by SN -> get Id -> Send Command.
                                
                                _logger.LogInformation("Processing ADMS record: User={User}, Time={Time}, SN={SN}", userId, checkTime, SN);
                                
                                // TODO: Implement Lookup and Mediator Call
                                // var device = await _deviceRepository.GetBySerialNumberAsync(SN);
                                // if (device != null) { 
                                //     await _mediator.Send(new RecordAttendanceCommand(userId, checkTime, device.Id.Value.ToString(), ...));
                                // }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing ADMS line: {Line}", line);
                    }
                }
            }
        }
        
        return Ok("OK");
    }
    
    [HttpGet("getrequest")]
    public IActionResult GetRequest([FromQuery] string SN)
    {
        return Ok("OK");
    }

    [HttpPost("devicecmd")]
    public IActionResult DeviceCmd([FromQuery] string SN)
    {
        return Ok("OK");
    }
}
