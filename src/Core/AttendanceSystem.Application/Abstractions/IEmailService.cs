public interface IEmailService
{
    Task SendAlertAsync(
        string subject, 
        string body, 
        CancellationToken cancellationToken = default);
}