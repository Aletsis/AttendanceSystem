using System.Windows.Controls;

namespace AttendanceSystem.WPF.Views.Common
{
    public partial class ReasonDialog : UserControl
    {
        public ReasonDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => ReasonBox.Focus();
        }
    }
}
