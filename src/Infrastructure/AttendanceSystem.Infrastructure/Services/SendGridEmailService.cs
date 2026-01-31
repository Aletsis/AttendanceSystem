using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Services;

public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IConfiguration configuration,
        ILogger<SendGridEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAlertAsync(
        string subject, 
        string body, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["SendGrid:ApiKey"];
            var fromEmail = _configuration["SendGrid:FromEmail"];
            var toEmail = _configuration["SendGrid:AlertEmail"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("SendGrid API Key no configurada. Email no enviado.");
                return;
            }

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(fromEmail, "Sistema de Asistencia");
            var to = new EmailAddress(toEmail);
            
            var msg = MailHelper.CreateSingleEmail(
                from, 
                to, 
                subject, 
                body, 
                $"<html><body><p>{body}</p></body></html>");

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email enviado exitosamente: {Subject}", subject);
            }
            else
            {
                _logger.LogError(
                    "Error al enviar email. Status: {StatusCode}", 
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al enviar email");
        }
    }

    public async Task SendAttendanceReportAsync(
        string recipientEmail,
        byte[] reportPdf,
        string reportName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["SendGrid:ApiKey"];
            var fromEmail = _configuration["SendGrid:FromEmail"];

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(fromEmail, "Sistema de Asistencia");
            var to = new EmailAddress(recipientEmail);
            
            var msg = MailHelper.CreateSingleEmail(
                from, 
                to, 
                $"Reporte de Asistencia - {reportName}",
                "Adjunto encontrarás el reporte de asistencia solicitado.",
                "<html><body><p>Adjunto encontrarás el reporte de asistencia solicitado.</p></body></html>");

            var file = Convert.ToBase64String(reportPdf);
            msg.AddAttachment(reportName, file, "application/pdf");

            await client.SendEmailAsync(msg, cancellationToken);

            _logger.LogInformation("Reporte enviado a {Email}", recipientEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar reporte por email");
        }
    }
}