using System.Net;
using System.Net.Mail;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly ISystemConfigurationRepository _configRepository;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        ISystemConfigurationRepository configRepository,
        ILogger<SmtpEmailService> logger)
    {
        _configRepository = configRepository;
        _logger = logger;
    }

    public async Task SendAlertAsync(
        string subject, 
        string body, 
        AlertLevel level = AlertLevel.SystemFailure,
        CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetConfigurationAsync(cancellationToken);
        
        if (config == null || !config.AreAlertsEnabled || string.IsNullOrEmpty(config.SmtpHost))
        {
            return;
        }

        string? recipients = level switch
        {
            AlertLevel.Absence => config.AbsenceAlertEmails,
            AlertLevel.Late => config.LateAlertEmails,
            AlertLevel.SystemFailure => config.SystemFailureAlertEmails,
            _ => config.SystemFailureAlertEmails
        };

        if (string.IsNullOrEmpty(recipients))
        {
            _logger.LogWarning("No hay destinatarios configurados para el nivel de alerta {Level}", level);
            return;
        }

        try
        {
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPassword);
            client.EnableSsl = config.SmtpEnableSsl;

            var mailMessage = new MailMessage
            {
                From = new MailAddress(config.SmtpUser ?? "noreply@sistema.com", config.CompanyName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            foreach (var recipient in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                mailMessage.To.Add(recipient.Trim());
            }

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Alerta {Level} enviada exitosamente a {Recipients}", level, recipients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar alerta {Level} por SMTP", level);
            throw;
        }
    }
    public async Task SendReportAsync(
        string subject, 
        string body, 
        string recipients, 
        IEnumerable<(string Name, byte[] Content)> attachments, 
        CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetConfigurationAsync(cancellationToken);
        
        if (config == null || string.IsNullOrEmpty(config.SmtpHost))
        {
            _logger.LogWarning("No se puede enviar el reporte. SMTP no configurado.");
            return;
        }

        if (string.IsNullOrEmpty(recipients))
        {
            _logger.LogWarning("No hay destinatarios configurados para el reporte.");
            return;
        }

        try
        {
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPassword);
            client.EnableSsl = config.SmtpEnableSsl;

            var mailMessage = new MailMessage
            {
                From = new MailAddress(config.SmtpUser ?? "noreply@sistema.com", config.CompanyName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            foreach (var recipient in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                mailMessage.To.Add(recipient.Trim());
            }

            var streams = new List<MemoryStream>();
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var stream = new MemoryStream(attachment.Content);
                    streams.Add(stream);
                    
                    string mimeType = "application/octet-stream";
                    if (attachment.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) mimeType = "application/pdf";
                    else if (attachment.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    
                    mailMessage.Attachments.Add(new Attachment(stream, attachment.Name, mimeType));
                }
            }

            await client.SendMailAsync(mailMessage, cancellationToken);
            
            // Dispose streams after sending
            foreach (var stream in streams)
            {
                stream.Dispose();
            }

            _logger.LogInformation("Reporte enviado exitosamente a {Recipients}", recipients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar reporte por SMTP");
        }
    }
}
