using System.Windows;
using System.Windows.Controls;

namespace AttendanceSystem.WPF.Behaviors
{
    /// <summary>
    /// Enlaza la contraseña de un PasswordBox (que no admite enlace directo) con una propiedad del ViewModel:
    /// <c>behaviors:PasswordBoxAssist.BoundPassword="{Binding SmtpPassword}"</c>.
    /// </summary>
    public static class PasswordBoxAssist
    {
        public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxAssist),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
            "IsUpdating", typeof(bool), typeof(PasswordBoxAssist));

        public static string GetBoundPassword(DependencyObject element) => (string)element.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject element, string value) => element.SetValue(BoundPasswordProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox box) return;
            box.PasswordChanged -= OnPasswordChanged;
            // Si el cambio viene de lo que se escribe, no se reescribe la caja (movería el cursor).
            if (!(bool)box.GetValue(IsUpdatingProperty)) box.Password = e.NewValue as string ?? string.Empty;
            box.PasswordChanged += OnPasswordChanged;
        }

        private static void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = (PasswordBox)sender;
            box.SetValue(IsUpdatingProperty, true);
            SetBoundPassword(box, box.Password);
            box.SetValue(IsUpdatingProperty, false);
        }
    }
}
