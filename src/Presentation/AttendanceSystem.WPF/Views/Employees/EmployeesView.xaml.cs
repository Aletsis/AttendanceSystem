using System.Windows.Controls;
using AttendanceSystem.WPF.ViewModels.Employees;
using Prism.Ioc;

namespace AttendanceSystem.WPF.Views.Employees
{
    public partial class EmployeesView : UserControl
    {
        public EmployeesView()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Container.Resolve<EmployeesViewModel>();
        }
    }
}
