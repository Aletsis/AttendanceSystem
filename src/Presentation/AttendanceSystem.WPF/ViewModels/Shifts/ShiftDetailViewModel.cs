using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

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
        private int _targetHours = 8;
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
            { ShiftType.Continuo, "Continuo" }
        };

        public Dictionary<ShiftType, string> DayShiftTypes { get; } = new()
        {
            { ShiftType.Matutino, "Matutino" },
            { ShiftType.Vespertino, "Vespertino" },
            { ShiftType.Nocturno, "Nocturno" },
            { ShiftType.Continuo, "Continuo" }
        };

        public KeyValuePair<ShiftType, string> SelectedShiftType
        {
            get => _selectedShiftType;
            set
            {
                if (SetProperty(ref _selectedShiftType, value))
                {
                    RaisePropertyChanged(nameof(IsStandardShift));
                    RaisePropertyChanged(nameof(IsContinuousShift));
                    RaisePropertyChanged(nameof(IsMixedShift));
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

        public int TargetHours
        {
            get => _targetHours;
            set => SetProperty(ref _targetHours, value);
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

        public bool IsStandardShift => SelectedShiftType.Key != ShiftType.Mixto && SelectedShiftType.Key != ShiftType.Continuo;
        public bool IsContinuousShift => SelectedShiftType.Key == ShiftType.Continuo;
        public bool IsMixedShift => SelectedShiftType.Key == ShiftType.Mixto;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<IDialogResult> RequestClose;

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
                _days.Add(new DayConfigViewModel
                {
                    DayOfWeek = kvp.Key,
                    DayName = kvp.Value,
                    StartDateTime = DateTime.Today.AddHours(9),
                    EndDateTime = DateTime.Today.AddHours(18)
                });
            }
        }

        private bool CanExecuteSave()
        {
            return !string.IsNullOrWhiteSpace(Name);
        }

        private void ExecuteSave()
        {
            TimeSpan startTime = StartDateTime?.TimeOfDay ?? TimeSpan.Zero;
            TimeSpan endTime = EndDateTime?.TimeOfDay ?? TimeSpan.Zero;
            
            if (SelectedShiftType.Key == ShiftType.Continuo)
            {
                startTime = TimeSpan.Zero;
                endTime = TimeSpan.FromHours(TargetHours);
            }

            TimeSpan durationEnd = endTime;
            if (durationEnd <= startTime)
                durationEnd = durationEnd.Add(TimeSpan.FromHours(24));
            
            TimeSpan workHours = durationEnd - startTime;

            var dayDtos = new List<ShiftDayDto>();
            if (SelectedShiftType.Key == ShiftType.Mixto)
            {
                foreach (var d in Days)
                {
                    var dStart = d.StartDateTime?.TimeOfDay ?? TimeSpan.Zero;
                    var dEnd = d.EndDateTime?.TimeOfDay ?? TimeSpan.Zero;
                    var dDur = dEnd <= dStart ? dEnd.Add(TimeSpan.FromHours(24)) : dEnd;
                    
                    dayDtos.Add(new ShiftDayDto(d.DayOfWeek, dStart, dEnd, dDur - dStart, d.ShiftType));
                }
            }

            var parameters = new DialogParameters
            {
                { "ShiftId", _shiftId },
                { "Name", Name },
                { "StartTime", startTime },
                { "ToleranceMinutes", ToleranceMinutes },
                { "WorkHours", workHours },
                { "ShiftType", SelectedShiftType.Key },
                { "Days", dayDtos },
                { "RoundingsEnabled", RoundingsEnabled },
                { "RoundingInterval", RoundingInterval }
            };

            RequestClose?.Invoke(new DialogResult(ButtonResult.OK, parameters));
        }

        private void ExecuteCancel()
        {
            RequestClose?.Invoke(new DialogResult(ButtonResult.Cancel));
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
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
                
                var end = start.Add(workHours);
                if (end.TotalDays >= 1) end = end.Subtract(TimeSpan.FromDays(1));
                EndDateTime = DateTime.Today.Add(end);

                if (type == ShiftType.Continuo)
                {
                    TargetHours = (int)workHours.TotalHours;
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

                var days = parameters.GetValue<IEnumerable<ShiftDayDto>>("Days");
                if (days != null)
                {
                    foreach (var day in days)
                    {
                        var vm = Days.FirstOrDefault(d => d.DayOfWeek == day.DayOfWeek);
                        if (vm != null)
                        {
                            vm.StartDateTime = DateTime.Today.Add(day.StartTime);
                            var dEnd = day.StartTime.Add(day.WorkHours);
                            if (dEnd.TotalDays >= 1) dEnd = dEnd.Subtract(TimeSpan.FromDays(1));
                            vm.EndDateTime = DateTime.Today.Add(dEnd);
                            vm.ShiftType = day.ShiftType;
                        }
                    }
                }

                Title = "Editar Turno";
            }
        }
    }

    public class DayConfigViewModel : BindableBase
    {
        private DateTime? _startDateTime;
        private DateTime? _endDateTime;
        private ShiftType _shiftType = ShiftType.Matutino;

        public DayOfWeek DayOfWeek { get; set; }
        public string DayName { get; set; } = string.Empty;

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

        public ShiftType ShiftType
        {
            get => _shiftType;
            set => SetProperty(ref _shiftType, value);
        }
    }
}
