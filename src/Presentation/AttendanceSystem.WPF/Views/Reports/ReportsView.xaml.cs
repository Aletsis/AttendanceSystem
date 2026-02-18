using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Reports;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Reports
{
    public partial class ReportsView : UserControl
    {
        public ReportsView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<ReportsViewModel>();
        }
    }
}
