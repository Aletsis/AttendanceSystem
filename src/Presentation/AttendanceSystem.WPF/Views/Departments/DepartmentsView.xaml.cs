using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Departments;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Departments
{
    public partial class DepartmentsView : UserControl
    {
        public DepartmentsView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<DepartmentsViewModel>();
        }
    }
}
