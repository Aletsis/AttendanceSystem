using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace AttendanceSystem.WPF.Services
{
    public interface IMessageService
    {
        Task ShowMessageAsync(string title, string message);
        Task<bool> ShowConfirmationAsync(string title, string message);
        Task ShowErrorAsync(string message);
        Task ShowSuccessAsync(string message);
        Task ShowWarningAsync(string message);

        /// <summary>Avisos de éxito visibles en la ventana principal (se cierran solos).</summary>
        ObservableCollection<ToastMessage> Toasts { get; }
    }

    public sealed record ToastMessage(string Text);

    public class MessageService : IMessageService
    {
        private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

        public ObservableCollection<ToastMessage> Toasts { get; } = new();

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

        /// <summary>
        /// Un guardado correcto no necesita confirmación: se muestra un aviso breve en lugar de una ventana que hay
        /// que cerrar. Los mensajes largos (p. ej. un resumen de importación) se siguen mostrando en ventana.
        /// </summary>
        public Task ShowSuccessAsync(string message)
        {
            if (message.Contains('\n') || System.Windows.Application.Current?.Dispatcher is not { } dispatcher)
            {
                MessageBox.Show(message, "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                return Task.CompletedTask;
            }

            dispatcher.Invoke(() =>
            {
                var toast = new ToastMessage(message);
                Toasts.Add(toast);
                var timer = new DispatcherTimer { Interval = ToastDuration };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Toasts.Remove(toast);
                };
                timer.Start();
            });
            return Task.CompletedTask;
        }

        public Task ShowWarningAsync(string message)
        {
            MessageBox.Show(message, "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }
    }
}
