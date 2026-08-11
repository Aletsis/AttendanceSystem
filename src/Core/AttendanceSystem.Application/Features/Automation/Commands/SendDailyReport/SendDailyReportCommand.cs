using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport;

public record SendDailyReportCommand() : IRequest<Result<bool>>;

public class SendDailyReportCommandHandler : IRequestHandler<SendDailyReportCommand, Result<bool>>
{
    private readonly IMediator _mediator;
    private readonly IReportExportService _reportExportService;
    private readonly IEmailService _emailService;
    private readonly ILogger<SendDailyReportCommandHandler> _logger;

    public SendDailyReportCommandHandler(
        IMediator mediator,
        IReportExportService reportExportService,
        IEmailService emailService,
        ILogger<SendDailyReportCommandHandler> logger)
    {
        _mediator = mediator;
        _reportExportService = reportExportService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(SendDailyReportCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Obtener la configuración para los destinatarios y la información de la empresa
            var configResult = await _mediator.Send(new Features.Configuration.Queries.GetSystemConfiguration.GetSystemConfigurationQuery(), cancellationToken);
            if (!configResult.IsSuccess || configResult.Value == null)
            {
                _logger.LogWarning("No se pudo obtener la configuración para el reporte diario automatizado.");
                return Result<bool>.Failure("Configuration not found");
            }

            var config = configResult.Value;

            if (!config.IsAutoReportEnabled || string.IsNullOrEmpty(config.AutoReportEmails))
            {
                _logger.LogInformation("El reporte automatizado está deshabilitado o no tiene destinatarios.");
                return Result<bool>.Success(true);
            }

            // Determinamos la fecha objetivo para el reporte
            var targetDate = config.AutoReportForToday ? DateTime.Today : DateTime.Today.AddDays(-1);

            _logger.LogInformation("Generando reporte diario automatizado para el {Date}", targetDate.ToString("yyyy-MM-dd"));

            var query = new GetAttendanceReportQuery(targetDate, targetDate);
            var reportData = await _mediator.Send(query, cancellationToken);

            if (reportData == null)
            {
                reportData = new List<DTOs.AttendanceReportViewDto>(); // Empty report
            }

            var pdfBytes = _reportExportService.GeneratePdf(
                reportData, 
                targetDate, 
                targetDate, 
                config.CompanyName, 
                config.CompanyLogo);

            var excelBytes = _reportExportService.GenerateExcel(
                reportData, 
                targetDate, 
                targetDate, 
                config.CompanyName, 
                config.CompanyLogo, 
                detailed: true);

            // Calcular estadísticas
            var totalAbsences = reportData.Count(x => x.IsAbsent);
            
            var absencesByBranch = reportData.Where(x => x.IsAbsent)
                .GroupBy(x => string.IsNullOrEmpty(x.BranchName) ? "Sin Sucursal" : x.BranchName)
                .Select(g => new { Branch = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count);

            var tardinessByBranch = reportData.Where(x => x.LateMinutes > 0)
                .GroupBy(x => string.IsNullOrEmpty(x.BranchName) ? "Sin Sucursal" : x.BranchName)
                .Select(g => new { Branch = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count);

            var overtimeByBranch = reportData.Where(x => x.RoundedOvertimeMinutes > 0)
                .GroupBy(x => string.IsNullOrEmpty(x.BranchName) ? "Sin Sucursal" : x.BranchName)
                .Select(g => new { Branch = g.Key, TotalMinutes = g.Sum(x => x.RoundedOvertimeMinutes) })
                .OrderByDescending(x => x.TotalMinutes);

            var topLate = reportData.Where(x => x.LateMinutes > 0)
                .OrderByDescending(x => x.LateMinutes)
                .Take(5);

            var topOvertime = reportData.Where(x => x.RoundedOvertimeMinutes > 0)
                .OrderByDescending(x => x.RoundedOvertimeMinutes)
                .Take(5);

            var subject = $"Reporte Diario de Asistencia - {targetDate.ToString("dd/MM/yyyy")}";
            
            var bodyBuilder = new System.Text.StringBuilder();
            bodyBuilder.AppendLine($"<h3>Resumen Diario de Asistencia: {targetDate.ToString("dd/MM/yyyy")}</h3>");
            bodyBuilder.AppendLine($"<p>A continuación se presenta un resumen de las incidencias del día. Puede consultar los detalles en los archivos adjuntos (PDF y Excel).</p>");
            
            bodyBuilder.AppendLine("<h4>Estadísticas Generales</h4>");
            bodyBuilder.AppendLine($"<ul><li><b>Total de Faltas:</b> {totalAbsences}</li></ul>");

            if (topLate.Any())
            {
                bodyBuilder.AppendLine("<h4>Personas con Mayor Retardo</h4><ul>");
                foreach (var item in topLate)
                    bodyBuilder.AppendLine($"<li>{item.EmployeeName} ({item.BranchName}): {item.LateMinutes} min</li>");
                bodyBuilder.AppendLine("</ul>");
            }

            if (topOvertime.Any())
            {
                bodyBuilder.AppendLine("<h4>Personas con Más Horas Extra</h4><ul>");
                foreach (var item in topOvertime)
                {
                    var hours = item.RoundedOvertimeMinutes / 60;
                    var mins = item.RoundedOvertimeMinutes % 60;
                    bodyBuilder.AppendLine($"<li>{item.EmployeeName} ({item.BranchName}): {hours}h {mins}m</li>");
                }
                bodyBuilder.AppendLine("</ul>");
            }

            if (absencesByBranch.Any())
            {
                bodyBuilder.AppendLine("<h4>Faltas por Sucursal</h4><ul>");
                foreach (var item in absencesByBranch)
                    bodyBuilder.AppendLine($"<li>{item.Branch}: {item.Count} faltas</li>");
                bodyBuilder.AppendLine("</ul>");
            }

            if (tardinessByBranch.Any())
            {
                bodyBuilder.AppendLine("<h4>Retardos por Sucursal</h4><ul>");
                foreach (var item in tardinessByBranch)
                {
                    bodyBuilder.AppendLine($"<li>{item.Branch}: {item.Count} personas con retardo</li>");
                }
                bodyBuilder.AppendLine("</ul>");
            }

            if (overtimeByBranch.Any())
            {
                bodyBuilder.AppendLine("<h4>Horas Extra por Sucursal</h4><ul>");
                foreach (var item in overtimeByBranch)
                {
                    var hours = item.TotalMinutes / 60;
                    var mins = item.TotalMinutes % 60;
                    bodyBuilder.AppendLine($"<li>{item.Branch}: {hours}h {mins}m en total</li>");
                }
                bodyBuilder.AppendLine("</ul>");
            }

            var attachments = new List<(string Name, byte[] Content)>
            {
                ($"Reporte_Asistencia_{targetDate.ToString("yyyyMMdd")}.pdf", pdfBytes),
                ($"Reporte_Asistencia_{targetDate.ToString("yyyyMMdd")}.xlsx", excelBytes)
            };

            await _emailService.SendReportAsync(
                subject, 
                bodyBuilder.ToString(), 
                config.AutoReportEmails, 
                attachments, 
                cancellationToken);

            _logger.LogInformation("Reporte diario automatizado generado y enviado con éxito.");
            
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar y enviar el reporte diario automatizado.");
            return Result<bool>.Failure(ex.Message);
        }
    }
}
