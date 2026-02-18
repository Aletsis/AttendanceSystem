using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Shifts;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Shifts
{
    public partial class ShiftsView : UserControl
    {
        public ShiftsView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<ShiftsViewModel>();
        }
    }
}
