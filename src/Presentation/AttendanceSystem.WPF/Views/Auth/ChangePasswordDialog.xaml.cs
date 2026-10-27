using System.Windows;
using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Auth;

namespace AttendanceSystem.WPF.Views.Auth
{
    public partial class ChangePasswordDialog : UserControl
    {
        public ChangePasswordDialog()
        {
            InitializeComponent();
        }

        // PasswordBox no admite enlace de datos: se pasa el texto al ViewModel al escribir.
        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ChangePasswordViewModel vm) return;
            vm.CurrentPassword = CurrentBox.Password;
            vm.NewPassword = NewBox.Password;
            vm.ConfirmPassword = ConfirmBox.Password;
        }
    }
}
