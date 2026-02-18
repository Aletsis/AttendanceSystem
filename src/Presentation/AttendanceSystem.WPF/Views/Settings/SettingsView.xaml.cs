using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Settings;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Settings
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<SettingsViewModel>();
        }
    }
}
