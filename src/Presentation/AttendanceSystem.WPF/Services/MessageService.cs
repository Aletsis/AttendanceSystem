using System.Windows;

namespace AttendanceSystem.WPF.Services
{
    public interface IMessageService
    {
        Task ShowMessageAsync(string title, string message);
        Task<bool> ShowConfirmationAsync(string title, string message);
        Task ShowErrorAsync(string message);
        Task ShowSuccessAsync(string message);
    }

    public class MessageService : IMessageService
    {
        public Task ShowMessageAsync(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        public Task ShowErrorAsync(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        public Task ShowSuccessAsync(string message)
        {
            MessageBox.Show(message, "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }
    }
}
