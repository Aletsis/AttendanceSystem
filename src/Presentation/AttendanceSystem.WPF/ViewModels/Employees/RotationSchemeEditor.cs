using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    /// <summary>
    /// Esquema rotativo del empleado, igual que en la web: edita el <see cref="RotationSchemeModel"/> compartido
    /// (misma validación y mismo comando) y avisa a la vista de cada cambio.
    /// </summary>
    public class RotationSchemeEditor : BindableBase
    {
        public RotationSchemeModel Model { get; } = new();

        public RotationSchemeEditor()
        {
            FixedRestDays = DayToggle.Week(SyncFixedRestDays);
            foreach (var day in FixedRestDays)
                day.IsSelected = Model.SelectedFixedRestDays.Contains((DayOfWeek)day.Value);
        }

        public IReadOnlyList<KeyValuePair<RotationSchemeType, string>> SchemeTypes { get; } = new List<KeyValuePair<RotationSchemeType, string>>
        {
            new(RotationSchemeType.Rotativo3x8, "Turnos 3x8 (3 turnos rotativos de 8 horas)"),
            new(RotationSchemeType.Rotativo2x8, "Turnos 2x8 (2 turnos rotativos de 8 horas)"),
            new(RotationSchemeType.Esquema4x3, "Esquema 4x3 (4 días de trabajo / 3 de descanso)"),
            new(RotationSchemeType.Esquema24x48, "Esquema 24x48 (guardia de 24 h / 48 h de descanso)")
        };

        public IReadOnlyList<KeyValuePair<RotationRestDayMode, string>> RestDayModes { get; } = new List<KeyValuePair<RotationRestDayMode, string>>
        {
            new(RotationRestDayMode.FixedDaysOfWeek, "Día(s) fijo(s) de la semana"),
            new(RotationRestDayMode.FromEmployeeProfile, "Día fijo del perfil del empleado"),
            new(RotationRestDayMode.AtEndOfCycle, "Al final de cada periodo de rotación")
        };

        public IReadOnlyList<KeyValuePair<int, string>> HorizonOptions { get; } = new List<KeyValuePair<int, string>>
        {
            new(6, "6 meses"), new(12, "1 año (recomendado)"), new(24, "2 años")
        };

        private IReadOnlyList<ShiftDto> _shifts = Array.Empty<ShiftDto>();
        public IReadOnlyList<ShiftDto> Shifts { get => _shifts; private set => SetProperty(ref _shifts, value); }

        public List<DayToggle> FixedRestDays { get; }

        public void InitializeShifts(IReadOnlyList<ShiftDto> shifts)
        {
            Shifts = shifts;
            Model.InitializeShifts(shifts);
            RaisePropertyChanged(nameof(Shift1));
            RaisePropertyChanged(nameof(Shift2));
            RaisePropertyChanged(nameof(Shift3));
            RaisePropertyChanged(nameof(BaseShift));
        }

        public RotationSchemeType SchemeType
        {
            get => Model.SchemeType;
            set
            {
                if (Model.SchemeType == value) return;
                Model.SchemeType = value;
                RaisePropertyChanged();
                RaiseLayout();
            }
        }

        public DateTime? StartDate
        {
            get => Model.StartDate;
            set { Model.StartDate = value; RaisePropertyChanged(); }
        }

        public DateTime? EndDate
        {
            get => Model.EndDate;
            set { Model.EndDate = value; RaisePropertyChanged(); }
        }

        public bool IsIndefinite
        {
            get => Model.IsIndefinite;
            set
            {
                if (Model.IsIndefinite == value) return;
                Model.IsIndefinite = value;
                EndDate = value ? null : (Model.StartDate ?? DateTime.Today).AddDays(30);
                RaisePropertyChanged();
            }
        }

        public int IndefiniteHorizonMonths
        {
            get => Model.IndefiniteHorizonMonths;
            set { Model.IndefiniteHorizonMonths = value; RaisePropertyChanged(); }
        }

        public Guid Shift1 { get => Model.Shift1; set { Model.Shift1 = value; RaisePropertyChanged(); } }
        public Guid Shift2 { get => Model.Shift2; set { Model.Shift2 = value; RaisePropertyChanged(); } }
        public Guid Shift3 { get => Model.Shift3; set { Model.Shift3 = value; RaisePropertyChanged(); } }
        public Guid BaseShift { get => Model.BaseShift; set { Model.BaseShift = value; RaisePropertyChanged(); } }

        public int DaysPerShift { get => Model.DaysPerShift; set { Model.DaysPerShift = value; RaisePropertyChanged(); } }
        public int RestDaysAfterShift { get => Model.RestDaysAfterShift; set { Model.RestDaysAfterShift = value; RaisePropertyChanged(); } }

        public RotationRestDayMode RestDayMode
        {
            get => Model.RestDayMode;
            set
            {
                if (Model.RestDayMode == value) return;
                Model.RestDayMode = value;
                RaisePropertyChanged();
                RaiseLayout();
            }
        }

        // Qué campos aplican a cada esquema (los mismos que muestra la web).
        public bool UsesRotatingShifts => SchemeType is RotationSchemeType.Rotativo3x8 or RotationSchemeType.Rotativo2x8;
        public bool UsesThirdShift => SchemeType == RotationSchemeType.Rotativo3x8;
        public bool UsesBaseShift => !UsesRotatingShifts;
        public bool ShowRestDaysAfterShift => UsesRotatingShifts && RestDayMode == RotationRestDayMode.AtEndOfCycle;
        public bool ShowFixedRestDays => UsesRotatingShifts && RestDayMode != RotationRestDayMode.AtEndOfCycle;
        public string FixedRestDaysLabel => RestDayMode == RotationRestDayMode.FromEmployeeProfile
            ? "Día(s) de descanso de respaldo (si el perfil no tiene)"
            : "Día(s) fijo(s) de descanso";
        public string Shift1Label => UsesThirdShift ? "Turno 1 (mañana)" : "Turno 1";
        public string Shift2Label => UsesThirdShift ? "Turno 2 (tarde)" : "Turno 2";
        public string BaseShiftLabel => SchemeType == RotationSchemeType.Esquema24x48 ? "Turno de guardia (24 horas)" : "Turno de trabajo (10 u 12 horas)";
        public string SchemeHint => SchemeType switch
        {
            RotationSchemeType.Rotativo3x8 => "Seleccione los 3 turnos en el orden en que rota el empleado (por ejemplo: mañana, tarde, noche).",
            RotationSchemeType.Rotativo2x8 => "Seleccione los 2 turnos en el orden en que rota el empleado.",
            RotationSchemeType.Esquema4x3 => "Ciclo continuo de 7 días: 4 días de jornada seguidos de 3 de descanso.",
            RotationSchemeType.Esquema24x48 => "Ciclo continuo de 3 días: 1 guardia de 24 horas seguida de 2 días de descanso.",
            _ => string.Empty
        };

        /// <summary>Carga el esquema vigente a partir del nombre que guarda el roster (como la web).</summary>
        public void SelectSchemeFromName(string schemeName)
        {
            if (schemeName.Contains("4x3", StringComparison.OrdinalIgnoreCase)) SchemeType = RotationSchemeType.Esquema4x3;
            else if (schemeName.Contains("24x48", StringComparison.OrdinalIgnoreCase)) SchemeType = RotationSchemeType.Esquema24x48;
            else if (schemeName.Contains("2x8", StringComparison.OrdinalIgnoreCase)) SchemeType = RotationSchemeType.Rotativo2x8;
            else if (schemeName.Contains("3x8", StringComparison.OrdinalIgnoreCase)) SchemeType = RotationSchemeType.Rotativo3x8;
        }

        private void SyncFixedRestDays() =>
            Model.SelectedFixedRestDays = FixedRestDays.Where(d => d.IsSelected).Select(d => (DayOfWeek)d.Value).ToHashSet();

        private void RaiseLayout()
        {
            RaisePropertyChanged(nameof(UsesRotatingShifts));
            RaisePropertyChanged(nameof(UsesThirdShift));
            RaisePropertyChanged(nameof(UsesBaseShift));
            RaisePropertyChanged(nameof(ShowRestDaysAfterShift));
            RaisePropertyChanged(nameof(ShowFixedRestDays));
            RaisePropertyChanged(nameof(FixedRestDaysLabel));
            RaisePropertyChanged(nameof(Shift1Label));
            RaisePropertyChanged(nameof(Shift2Label));
            RaisePropertyChanged(nameof(BaseShiftLabel));
            RaisePropertyChanged(nameof(SchemeHint));
        }
    }
}
