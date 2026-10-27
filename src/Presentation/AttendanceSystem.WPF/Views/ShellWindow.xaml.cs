using AttendanceSystem.WPF.ViewModels;
using MahApps.Metro.Controls;

namespace AttendanceSystem.WPF.Views
{
    public partial class ShellWindow : MetroWindow
    {
        public ShellWindow(ShellViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += (_, _) => viewModel.AttachToMainRegion();
        }
    }
}
