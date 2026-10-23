using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Shifts;
using AttendanceSystem.Domain.Enumerations;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Shifts
{
    public class ShiftDetailViewModel : BindableBase, IDialogAware
    {
        private Guid? _shiftId;
        private string _name = string.Empty;
        private int _toleranceMinutes;
        private KeyValuePair<ShiftType, string> _selectedShiftType;
        private DateTime? _startDateTime = DateTime.Today.AddHours(9);
        private DateTime? _endDateTime = DateTime.Today.AddHours(18);
        private DateTime? _flexWindowEndDateTime = DateTime.Today.AddHours(10);
        private int _targetHours = 8;
        private int _weeklyHours = 40;
        private string _title = "Nuevo Turno";
        private ObservableCollection<DayConfigViewModel> _days = new();
        private bool _roundingsEnabled;
        private int _roundingInterval = 15;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int ToleranceMinutes
        {
            get => _toleranceMinutes;
            set => SetProperty(ref _toleranceMinutes, value);
        }

        public Dictionary<ShiftType, string> ShiftTypes { get; } = new()
        {
            { ShiftType.Matutino, "Matutino" },
            { ShiftType.Vespertino, "Vespertino" },
            { ShiftType.Nocturno, "Nocturno" },
            { ShiftType.Mixto, "Mixto" },
            { ShiftType.Flexible, "Flexible" },
            { ShiftType.Partido, "Partido / Doble Turno" },
            { ShiftType.Rotativo, "Rotativo" }
        };

        // Decisión 5: cada día del mixto usa un horario existente; todos los tipos excepto Mixto y Rotativo.
        // Caso 9: no hay opción de descanso: los descansos son los del empleado.
        public List<KeyValuePair<ShiftType?, string>> DayShiftTypes { get; } = new()
        {
            new(ShiftType.Matutino, "Matutino"),
            new(ShiftType.Vespertino, "Vespertino"),
            new(ShiftType.Nocturno, "Nocturno"),
            new(ShiftType.Flexible, "Flexible"),
            new(ShiftType.Partido, "Partido / Doble Turno")
        };

        private List<ShiftDto> _availableShifts = new();

        private IEnumerable<ScheduleOption> SchedulesOfType(ShiftType type) =>
            _availableShifts
                .Where(x => x.ShiftType == type && x.Id != _shiftId)
                .OrderBy(x => x.StartTime)
                .Select(x => new ScheduleOption(x.Id, x.ShiftType == ShiftType.Partido && x.SecondBlockStartTime.HasValue && x.SecondBlockEndTime.HasValue
                    ? $"{x.Name} ({x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm} y {x.SecondBlockStartTime:hh\\:mm}–{x.SecondBlockEndTime:hh\\:mm})"
                    : $"{x.Name} ({x.StartTime:hh\\:mm}–{x.EndTime:hh\\:mm})"));

        public KeyValuePair<ShiftType, string> SelectedShiftType
        {
            get => _selectedShiftType;
            set
            {
                if (SetProperty(ref _selectedShiftType, value))
                {
                    RaisePropertyChanged(nameof(IsStandardShift));
                    RaisePropertyChanged(nameof(IsMixedShift));
                    RaisePropertyChanged(nameof(IsFlexibleShift));
                    RaisePropertyChanged(nameof(IsSplitShift));
                }
            }
        }

        public DateTime? StartDateTime
        {
            get => _startDateTime;
            set => SetProperty(ref _startDateTime, value);
        }

        public DateTime? EndDateTime
        {
            get => _endDateTime;
            set => SetProperty(ref _endDateTime, value);
        }

        public DateTime? FlexWindowEndDateTime
        {
            get => _flexWindowEndDateTime;
            set => SetProperty(ref _flexWindowEndDateTime, value);
        }

        public int TargetHours
        {
            get => _targetHours;
            set => SetProperty(ref _targetHours, value);
        }

        public int WeeklyHours
        {
            get => _weeklyHours;
            set => SetProperty(ref _weeklyHours, value);
        }

        public ObservableCollection<DayConfigViewModel> Days
        {
            get => _days;
            set => SetProperty(ref _days, value);
        }

        public bool RoundingsEnabled
        {
            get => _roundingsEnabled;
            set => SetProperty(ref _roundingsEnabled, value);
        }

        public int RoundingInterval
        {
            get => _roundingInterval;
            set => SetProperty(ref _roundingInterval, value);
        }

        private PunchTrackingMode _punchTrackingMode = PunchTrackingMode.SingleInterval;
        private bool _hasEntryWindow = true;

        public PunchTrackingMode PunchTrackingMode
        {
            get => _punchTrackingMode;
            set
            {
                if (SetProperty(ref _punchTrackingMode, value))
                {
                    RaisePropertyChanged(nameof(IsSingleInterval));
                    RaisePropertyChanged(nameof(IsMultiInterval));
                }
            }
        }

        public bool IsSingleInterval
        {
            get => _punchTrackingMode == PunchTrackingMode.SingleInterval;
            set
            {
                if (value) PunchTrackingMode = PunchTrackingMode.SingleInterval;
            }
        }

        public bool IsMultiInterval
        {
            get => _punchTrackingMode == PunchTrackingMode.MultiInterval;
            set
            {
                if (value) PunchTrackingMode = PunchTrackingMode.MultiInterval;
            }
        }

        public bool HasEntryWindow
        {
            get => _hasEntryWindow;
            set => SetProperty(ref _hasEntryWindow, value);
        }

        private bool _isWeeklyFlexibleMode;
        public bool IsWeeklyFlexibleMode
        {
            get => _isWeeklyFlexibleMode;
            set
            {
                if (SetProperty(ref _isWeeklyFlexibleMode, value))
                {
                    RaisePropertyChanged(nameof(IsDailyFlexibleMode));
                }
            }
        }

        public bool IsDailyFlexibleMode
        {
            get => !_isWeeklyFlexibleMode;
            set
            {
                if (SetProperty(ref _isWeeklyFlexibleMode, !value))
                {
                    RaisePropertyChanged(nameof(IsWeeklyFlexibleMode));
                }
            }
        }

        private DateTime? _secondBlockStartDateTime = DateTime.Today.AddHours(16);
        private DateTime? _secondBlockEndDateTime = DateTime.Today.AddHours(20);
        private int _secondBlockToleranceMinutes = 10;

        public DateTime? SecondBlockStartDateTime
        {
            get => _secondBlockStartDateTime;
            set => SetProperty(ref _secondBlockStartDateTime, value);
        }

        public DateTime? SecondBlockEndDateTime
        {
            get => _secondBlockEndDateTime;
            set => SetProperty(ref _secondBlockEndDateTime, value);
        }

        public int SecondBlockToleranceMinutes
        {
            get => _secondBlockToleranceMinutes;
            set => SetProperty(ref _secondBlockToleranceMinutes, value);
        }

        public bool IsStandardShift => SelectedShiftType.Key != ShiftType.Mixto && SelectedShiftType.Key != ShiftType.Flexible && SelectedShiftType.Key != ShiftType.Partido;
        public bool IsMixedShift => SelectedShiftType.Key == ShiftType.Mixto;
        public bool IsFlexibleShift => SelectedShiftType.Key == ShiftType.Flexible;
        public bool IsSplitShift => SelectedShiftType.Key == ShiftType.Partido;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; }

        public ShiftDetailViewModel()
        {
            SelectedShiftType = ShiftTypes.First();
            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave)
                .ObservesProperty(() => Name);

            CancelCommand = new DelegateCommand(ExecuteCancel);

            InitDays();
        }

        private void InitDays()
        {
            var spanishDays = new Dictionary<DayOfWeek, string>
            {
                { DayOfWeek.Monday, "Lunes" },
                { DayOfWeek.Tuesday, "Martes" },
                { DayOfWeek.Wednesday, "Miércoles" },
                { DayOfWeek.Thursday, "Jueves" },
                { DayOfWeek.Friday, "Viernes" },
                { DayOfWeek.Saturday, "Sábado" },
                { DayOfWeek.Sunday, "Domingo" }
            };

            foreach (var kvp in spanishDays)
            {
                _days.Add(new DayConfigViewModel(SchedulesOfType)
                {
                    DayOfWeek = kvp.Key,
                    DayName = kvp.Value,
                    ScheduleChanged = () => UpdateMixedScheduleMessage()
                });
            }
        }

        private bool CanExecuteSave()
        {
            return !string.IsNullOrWhiteSpace(Name);
        }

        private string? _mixedScheduleMessage;

        /// <summary>Caso 9: días sin horario (bloquean el guardado) y avisos de días que terminan a menos de 2 h del siguiente.</summary>
        public string? MixedScheduleMessage
        {
            get => _mixedScheduleMessage;
            private set => SetProperty(ref _mixedScheduleMessage, value);
        }

        private void UpdateMixedScheduleMessage(bool includeMissing = false)
        {
            var messages = new List<string>();
            if (includeMissing)
            {
                var missing = MixedShiftSchedule.MissingDays(Days.Where(d => d.DayShiftId.HasValue).Select(d => d.DayOfWeek));
                if (missing.Count > 0) messages.Add(MixedShiftSchedule.MissingDaysMessage(missing));
            }
            var schedules = _availableShifts.ToDictionary(x => x.Id);
            var days = Days
                .Where(d => d.DayShiftId is { } id && schedules.ContainsKey(id))
                .ToDictionary(d => d.DayOfWeek, d => schedules[d.DayShiftId!.Value]);
            messages.AddRange(MixedShiftSchedule.AdjacencyWarnings(days));
            MixedScheduleMessage = messages.Count > 0 ? string.Join(Environment.NewLine, messages) : null;
        }

        private void ExecuteSave()
        {
            TimeSpan startTime = StartDateTime?.TimeOfDay ?? TimeSpan.Zero;
            TimeSpan endTime = EndDateTime?.TimeOfDay ?? TimeSpan.Zero;
            TimeSpan? flexEnd = null;

            if (SelectedShiftType.Key == ShiftType.Flexible)
            {
                if (HasEntryWindow)
                {
                    startTime = StartDateTime?.TimeOfDay ?? new TimeSpan(8, 0, 0);
                    flexEnd = FlexWindowEndDateTime?.TimeOfDay ?? startTime;
                    endTime = flexEnd.Value.Add(TimeSpan.FromHours(TargetHours));
                }
                else
                {
                    startTime = TimeSpan.Zero;
                    flexEnd = null;
                    endTime = TimeSpan.FromHours(TargetHours);
                }
            }

            TimeSpan durationEnd = endTime;
            if (durationEnd <= startTime)
                durationEnd = durationEnd.Add(TimeSpan.FromHours(24));

            TimeSpan workHours;
            TimeSpan? weekly = null;
            TimeSpan? b2Start = null;
            TimeSpan? b2End = null;
            int? b2Tol = null;

            if (SelectedShiftType.Key == ShiftType.Flexible)
            {
                if (IsDailyFlexibleMode)
                {
                    workHours = TimeSpan.FromHours(TargetHours);
                    weekly = null;
                }
                else
                {
                    workHours = TimeSpan.Zero;
                    weekly = TimeSpan.FromHours(WeeklyHours);
                }
            }
            else if (SelectedShiftType.Key == ShiftType.Partido)
            {
                b2Start = SecondBlockStartDateTime?.TimeOfDay ?? new TimeSpan(16, 0, 0);
                b2End = SecondBlockEndDateTime?.TimeOfDay ?? new TimeSpan(20, 0, 0);
                b2Tol = SecondBlockToleranceMinutes;

                var b1Dur = durationEnd - startTime;
                var b2DurEnd = b2End.Value <= b2Start.Value ? b2End.Value.Add(TimeSpan.FromHours(24)) : b2End.Value;
                var b2Dur = b2DurEnd - b2Start.Value;
                workHours = b1Dur;
            }
            else
            {
                workHours = durationEnd - startTime;
            }

            var dayDtos = new List<ShiftDayDto>();
            if (SelectedShiftType.Key == ShiftType.Mixto)
            {
                // Caso 9: los 7 días llevan horario (el servidor también lo valida).
                if (Days.Any(d => !d.DayShiftId.HasValue))
                {
                    UpdateMixedScheduleMessage(includeMissing: true);
                    return;
                }
                foreach (var d in Days.Where(d => d.Type.HasValue))
                {
                    var schedule = _availableShifts.FirstOrDefault(x => x.Id == d.DayShiftId);
                    dayDtos.Add(new ShiftDayDto(d.DayOfWeek,
                        schedule?.StartTime ?? TimeSpan.Zero,
                        schedule?.EndTime ?? TimeSpan.Zero,
                        schedule?.WorkHours ?? TimeSpan.Zero,
                        d.Type!.Value,
                        d.DayShiftId,
                        schedule?.Name));
                }
            }

            bool effectiveHasEntryWindow = SelectedShiftType.Key == ShiftType.Flexible ? HasEntryWindow : true;

            var parameters = new DialogParameters
            {
                { "Name", Name },
                { "StartTime", startTime },
                { "ToleranceMinutes", ToleranceMinutes },
                { "WorkHours", workHours },
                { "ShiftType", SelectedShiftType.Key },
                { "Days", dayDtos },
                { "RoundingsEnabled", RoundingsEnabled },
                { "RoundingInterval", RoundingInterval },
                { "FlexWindowEndTime", flexEnd! },
                { "WeeklyWorkHours", weekly! },
                { "SecondBlockStartTime", b2Start! },
                { "SecondBlockEndTime", b2End! },
                { "SecondBlockToleranceMinutes", b2Tol! },
                { "PunchTrackingMode", PunchTrackingMode },
                { "HasEntryWindow", effectiveHasEntryWindow }
            };

            if (_shiftId.HasValue)
            {
                parameters.Add("ShiftId", _shiftId.Value);
            }

            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        private void ExecuteCancel()
        {
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("AvailableShifts"))
            {
                _availableShifts = parameters.GetValue<IEnumerable<ShiftDto>>("AvailableShifts")?.ToList() ?? new();
            }

            if (parameters.ContainsKey("ShiftId"))
            {
                _shiftId = parameters.GetValue<Guid>("ShiftId");
                Name = parameters.GetValue<string>("Name");
                ToleranceMinutes = parameters.GetValue<int>("ToleranceMinutes");

                var type = parameters.GetValue<ShiftType>("ShiftType");
                SelectedShiftType = ShiftTypes.FirstOrDefault(t => t.Key == type);

                var start = parameters.GetValue<TimeSpan>("StartTime");
                var workHours = parameters.GetValue<TimeSpan>("WorkHours");

                StartDateTime = DateTime.Today.Add(start);

                if (parameters.ContainsKey("EndTime") && parameters.GetValue<TimeSpan?>("EndTime").HasValue)
                {
                    EndDateTime = DateTime.Today.Add(parameters.GetValue<TimeSpan>("EndTime"));
                }
                else
                {
                    var end = start.Add(workHours);
                    if (end.TotalDays >= 1) end = end.Subtract(TimeSpan.FromDays(1));
                    EndDateTime = DateTime.Today.Add(end);
                }

                if (type == ShiftType.Flexible)
                {
                    if (parameters.ContainsKey("WeeklyWorkHours"))
                    {
                        var weekly = parameters.GetValue<TimeSpan?>("WeeklyWorkHours");
                        if (weekly.HasValue && weekly.Value > TimeSpan.Zero)
                        {
                            WeeklyHours = (int)weekly.Value.TotalHours;
                            IsWeeklyFlexibleMode = true;
                        }
                        else
                        {
                            IsWeeklyFlexibleMode = false;
                        }
                    }
                    else
                    {
                        IsWeeklyFlexibleMode = false;
                    }

                    if (workHours > TimeSpan.Zero)
                    {
                        TargetHours = (int)workHours.TotalHours;
                    }
                }

                if (parameters.ContainsKey("FlexWindowEndTime"))
                {
                    var flexEnd = parameters.GetValue<TimeSpan?>("FlexWindowEndTime");
                    if (flexEnd.HasValue)
                        FlexWindowEndDateTime = DateTime.Today.Add(flexEnd.Value);
                }

                if (parameters.ContainsKey("SecondBlockStartTime"))
                {
                    var b2s = parameters.GetValue<TimeSpan?>("SecondBlockStartTime");
                    if (b2s.HasValue)
                        SecondBlockStartDateTime = DateTime.Today.Add(b2s.Value);
                }
                if (parameters.ContainsKey("SecondBlockEndTime"))
                {
                    var b2e = parameters.GetValue<TimeSpan?>("SecondBlockEndTime");
                    if (b2e.HasValue)
                        SecondBlockEndDateTime = DateTime.Today.Add(b2e.Value);
                }
                if (parameters.ContainsKey("SecondBlockToleranceMinutes"))
                {
                    var b2t = parameters.GetValue<int?>("SecondBlockToleranceMinutes");
                    if (b2t.HasValue)
                        SecondBlockToleranceMinutes = b2t.Value;
                }

                if (parameters.ContainsKey("RoundingsEnabled"))
                {
                    RoundingsEnabled = parameters.GetValue<bool>("RoundingsEnabled");
                }
                if (parameters.ContainsKey("RoundingInterval"))
                {
                    var interval = parameters.GetValue<int>("RoundingInterval");
                    if (interval > 0)
                        RoundingInterval = interval;
                }

                if (parameters.ContainsKey("PunchTrackingMode"))
                {
                    PunchTrackingMode = parameters.GetValue<PunchTrackingMode>("PunchTrackingMode");
                }
                if (parameters.ContainsKey("HasEntryWindow"))
                {
                    HasEntryWindow = parameters.GetValue<bool>("HasEntryWindow");
                }

                var days = parameters.GetValue<IEnumerable<ShiftDayDto>>("Days");
                if (days != null)
                {
                    foreach (var day in days)
                    {
                        var vm = Days.FirstOrDefault(d => d.DayOfWeek == day.DayOfWeek);
                        if (vm != null)
                        {
                            vm.Type = day.ShiftType;
                            vm.DayShiftId = day.DayShiftId;
                            vm.Legacy = day.DayShiftId == null;
                        }
                    }
                }

                Title = "Editar Turno";
            }
        }
    }

    public sealed record ScheduleOption(Guid Id, string Label);

    public class DayConfigViewModel : BindableBase
    {
        private readonly Func<ShiftType, IEnumerable<ScheduleOption>> _schedulesOfType;
        private ShiftType? _type;
        private Guid? _dayShiftId;
        private bool _legacy;

        public DayConfigViewModel(Func<ShiftType, IEnumerable<ScheduleOption>> schedulesOfType) => _schedulesOfType = schedulesOfType;

        /// <summary>Se invoca al cambiar el horario del día (avisos de la plantilla).</summary>
        public Action? ScheduleChanged { get; set; }

        public DayOfWeek DayOfWeek { get; set; }
        public string DayName { get; set; } = string.Empty;

        /// <summary>Tipo elegido para filtrar horarios; null = sin elegir.</summary>
        public ShiftType? Type
        {
            get => _type;
            set
            {
                if (!SetProperty(ref _type, value)) return;
                AvailableSchedules = new ObservableCollection<ScheduleOption>(value.HasValue ? _schedulesOfType(value.Value) : Enumerable.Empty<ScheduleOption>());
                RaisePropertyChanged(nameof(AvailableSchedules));
                RaisePropertyChanged(nameof(HasSchedule));
                if (DayShiftId.HasValue && !AvailableSchedules.Any(o => o.Id == DayShiftId)) DayShiftId = null;
            }
        }

        public bool HasSchedule => Type.HasValue;

        public ObservableCollection<ScheduleOption> AvailableSchedules { get; private set; } = new();

        public Guid? DayShiftId
        {
            get => _dayShiftId;
            set
            {
                if (SetProperty(ref _dayShiftId, value)) ScheduleChanged?.Invoke();
            }
        }

        /// <summary>Día guardado con horas por día (formato anterior), sin horario asignado.</summary>
        public bool Legacy
        {
            get => _legacy;
            set => SetProperty(ref _legacy, value);
        }
    }
}
