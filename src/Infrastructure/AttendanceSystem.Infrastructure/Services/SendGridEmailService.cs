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
        AlertLevel level = AlertLevel.SystemFailure,
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
                _logger.LogInformation("Email de alerta ({Level}) enviado exitosamente: {Subject}", level, subject);
            }
            else
            {
                _logger.LogError(
                    "Error al enviar email de alerta ({Level}). Status: {StatusCode}", 
                    level,
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al enviar email de alerta ({Level})", level);
        }
    }

    public async Task SendReportAsync(
        string subject, 
        string body, 
        string recipients, 
        IEnumerable<(string Name, byte[] Content)> attachments, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["SendGrid:ApiKey"];
            var fromEmail = _configuration["SendGrid:FromEmail"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("SendGrid API Key no configurada. Reporte no enviado.");
                return;
            }

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(fromEmail, "Sistema de Asistencia");
            
            var msg = new SendGridMessage
            {
                From = from,
                Subject = subject,
                PlainTextContent = body,
                HtmlContent = $"<html><body>{body}</body></html>"
            };

            foreach (var recipient in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                msg.AddTo(new EmailAddress(recipient.Trim()));
            }
            
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var file = Convert.ToBase64String(attachment.Content);
                    string mimeType = "application/octet-stream";
                    if (attachment.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) mimeType = "application/pdf";
                    else if (attachment.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    
                    msg.AddAttachment(attachment.Name, file, mimeType);
                }
            }

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Reporte enviado exitosamente a {Recipients}", recipients);
            }
            else
            {
                _logger.LogError("Error al enviar reporte ({StatusCode})", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar reporte por email");
        }
    }
}
