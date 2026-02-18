using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Positions;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Positions
{
    public partial class PositionsView : UserControl
    {
        public PositionsView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<PositionsViewModel>();
        }
    }
}
