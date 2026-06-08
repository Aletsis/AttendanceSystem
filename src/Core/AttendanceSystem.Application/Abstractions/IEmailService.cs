public enum AlertLevel
{
    Absence,
    Late,
    SystemFailure
}

public interface IEmailService
{
    Task SendAlertAsync(
        string subject, 
        string body, 
        AlertLevel level = AlertLevel.SystemFailure,
        CancellationToken cancellationToken = default);

    Task SendReportAsync(
        string subject, 
        string body, 
        string recipients, 
        IEnumerable<(string Name, byte[] Content)> attachments, 
        CancellationToken cancellationToken = default);
}
