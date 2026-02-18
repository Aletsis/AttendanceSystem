using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Branches;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Branches
{
    public partial class BranchesView : UserControl
    {
        public BranchesView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<BranchesViewModel>();
        }
    }
}
