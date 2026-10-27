using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace AttendanceSystem.WPF.Converters
{
    public class SuccessToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool success)
            {
                return success ? PackIconMaterialKind.CheckCircle : PackIconMaterialKind.CloseCircle;
            }
            return PackIconMaterialKind.HelpCircle;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class SuccessToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool success)
            {
                return success ? Brushes.Green : Brushes.Red;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
