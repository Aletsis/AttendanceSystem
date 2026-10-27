using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Attendance;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Attendance
{
    public partial class PunchLogView : UserControl
    {
        public PunchLogView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<PunchLogViewModel>();
        }
    }
}
