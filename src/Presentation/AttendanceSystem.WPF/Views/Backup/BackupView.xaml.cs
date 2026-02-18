using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Backup;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Backup
{
    public partial class BackupView : UserControl
    {
        public BackupView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<BackupViewModel>();
        }
    }
}
