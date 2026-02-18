using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Devices;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Devices
{
    public partial class DevicesView : UserControl
    {
        public DevicesView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<DevicesViewModel>();
        }
    }
}
