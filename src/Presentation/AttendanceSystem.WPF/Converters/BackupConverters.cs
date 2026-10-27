using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.Converters
{
    public class BytesToSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = bytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BackupTypeToBrushConverter : IValueConverter
    {
        private static readonly Brush SafetySnapshot = new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x04));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            "Full" => Brushes.RoyalBlue,
            // Copia automática de la base antes de una restauración: no es un respaldo programado ni manual.
            BackupDto.SafetySnapshotType => SafetySnapshot,
            string => Brushes.Teal,
            _ => Brushes.Gray
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BackupTypeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            "Full" => "Completo",
            "DatabaseOnly" => "Solo BD",
            BackupDto.SafetySnapshotType => "Previo a restaurar",
            _ => value?.ToString() ?? string.Empty
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
