using System.Windows;
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

        // Las columnas de la vista previa cambian según el reporte: se generan solas y aquí sólo se ajusta su aspecto.
        private void OnPreviewColumnGenerating(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is not DataGridTextColumn column) return;

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 6, 4, 6)));
            switch (e.PropertyName)
            {
                case "Empleado":
                    column.MinWidth = 180;
                    break;
                case "Observaciones":
                    // Una línea por día, como en el recuadro de la tarjeta.
                    style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    column.MinWidth = 320;
                    break;
            }
            column.ElementStyle = style;
        }
    }
}
