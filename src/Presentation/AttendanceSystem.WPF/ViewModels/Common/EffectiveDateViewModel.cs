using System;
using System.Threading.Tasks;
using System.Windows.Input;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Common
{
    /// <summary>
    /// Caso 15: fecha desde la que aplica un cambio que afecta el cálculo (horario o configuración), con el aviso de lo
    /// que se recalculará. Parámetros: "Title", "Message" y "Impact" (Func&lt;DateTime, Task&lt;RecalculationImpactDto&gt;&gt;);
    /// devuelve "EffectiveDate".
    /// </summary>
    public class EffectiveDateViewModel : BindableBase, IDialogAware
    {
        private Func<DateTime, Task<RecalculationImpactDto>>? _impact;
        private DateTime? _effectiveDate = DateTime.Today;
        private string _impactText = string.Empty;
        private string _message = string.Empty;
        private string _title = "Aplicar cambio";

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string ImpactText { get => _impactText; private set => SetProperty(ref _impactText, value); }

        public DateTime? EffectiveDate
        {
            get => _effectiveDate;
            set
            {
                if (SetProperty(ref _effectiveDate, value))
                {
                    _ = RefreshImpactAsync();
                    ((DelegateCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public DialogCloseListener RequestClose { get; }

        public EffectiveDateViewModel()
        {
            SaveCommand = new DelegateCommand(Save, () => EffectiveDate.HasValue);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        private async Task RefreshImpactAsync()
        {
            if (_impact == null || EffectiveDate is not { } date) return;
            ImpactText = "Calculando…";
            try
            {
                ImpactText = (await _impact(date.Date)).Describe();
            }
            catch (Exception ex)
            {
                ImpactText = $"No se pudo calcular el impacto: {ex.Message}";
            }
        }

        private void Save()
        {
            if (EffectiveDate is not { } date) return;
            RequestClose.Invoke(new DialogParameters { { "EffectiveDate", date.Date } }, ButtonResult.OK);
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Title")) Title = parameters.GetValue<string>("Title");
            if (parameters.ContainsKey("Message")) Message = parameters.GetValue<string>("Message");
            if (parameters.ContainsKey("Impact")) _impact = parameters.GetValue<Func<DateTime, Task<RecalculationImpactDto>>>("Impact");
            _ = RefreshImpactAsync();
        }
    }
}
