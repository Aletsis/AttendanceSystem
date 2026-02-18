using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Dashboard;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Dashboard
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            // Get ViewModel from DI container
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<DashboardViewModel>();
        }
    }
}
