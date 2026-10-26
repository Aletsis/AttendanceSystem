using System;
using System.Globalization;
using System.Windows.Data;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.WPF.Converters
{
    /// <summary>Texto en español de los valores de enumeración que se muestran en pantalla (mismos textos que la web).</summary>
    public static class EnumDisplay
    {
        public static string Text(object? value) => value switch
        {
            null => string.Empty,
            EmployeeStatus.Alta => "Alta",
            EmployeeStatus.Baja => "Baja",
            EmployeeStatus.BajaTemporal => "Baja temporal",
            EmployeeStatus.Vacaciones => "Vacaciones",
            EmployeeStatus.Incapacidad => "Incapacidad",
            Gender.Male => "Masculino",
            Gender.Female => "Femenino",
            ShiftType.Partido => "Partido / Doble turno",
            OvertimeCalculationMethod.NoRounding => "Sin redondeo",
            OvertimeCalculationMethod.RoundByHalfHour => "Medias horas",
            OvertimeCalculationMethod.RoundByHour => "Horas completas",
            OvertimeCapType.None => "Sin tope",
            OvertimeCapType.Daily => "Diario",
            OvertimeCapType.Period => "Por periodo",
            WorkPeriodMode.Weekly => "Semanal",
            WorkPeriodMode.Fortnightly => "Quincenal",
            WorkPeriodMode.Monthly => "Mensual",
            DeviceDownloadMethod.Sdk => "SDK (conexión directa)",
            DeviceDownloadMethod.Adms => "ADMS (el reloj envía)",
            DayOfWeek day => CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetDayName(day) is { Length: > 0 } name
                ? char.ToUpper(name[0]) + name[1..]
                : day.ToString(),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>Muestra un valor de enumeración con su texto en español (combos, columnas, etiquetas).</summary>
    public class EnumDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => EnumDisplay.Text(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
