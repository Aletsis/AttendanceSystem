using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    /// <summary>Día de la semana que se marca o desmarca (descansos del empleado o del esquema rotativo).</summary>
    public class DayToggle : BindableBase
    {
        private readonly Action? _changed;
        private bool _isSelected;

        public DayToggle(int value, string name, Action? changed = null)
        {
            Value = value;
            Name = name;
            _changed = changed;
        }

        /// <summary>Número del día: 0 = domingo … 6 = sábado (igual en WeekDay y DayOfWeek).</summary>
        public int Value { get; }
        public string Name { get; }
        public string ShortName => Name[..3];

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value)) _changed?.Invoke();
            }
        }

        /// <summary>Lunes a domingo, en el orden en que se leen en la semana laboral.</summary>
        public static List<DayToggle> Week(Action? changed = null) => new()
        {
            new(1, "Lunes", changed), new(2, "Martes", changed), new(3, "Miércoles", changed), new(4, "Jueves", changed),
            new(5, "Viernes", changed), new(6, "Sábado", changed), new(0, "Domingo", changed)
        };
    }
}
