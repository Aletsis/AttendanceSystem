using System.Collections.ObjectModel;
using System.Windows.Input;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Attendance
{
    /// <summary>
    /// Marcaje manual (mismo comando que la página de marcajes manuales de la web): se registra la hora civil del
    /// marcaje con un motivo obligatorio y después se recalcula ese día del empleado.
    /// </summary>
    public class ManualPunchViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private EmployeeOption? _selectedEmployee;
        private DateTime? _date = DateTime.Today;
        private DateTime? _time = DateTime.Today.AddHours(DateTime.Now.Hour);
        private string _reason = string.Empty;
        private string? _errorMessage;
        private bool _isSaving;

        public ManualPunchViewModel(IMediator mediator)
        {
            _mediator = mediator;
            SaveCommand = new DelegateCommand(async () => await SaveAsync());
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public ObservableCollection<EmployeeOption> Employees { get; } = new();
        public EmployeeOption? SelectedEmployee { get => _selectedEmployee; set => SetProperty(ref _selectedEmployee, value); }
        public DateTime? Date { get => _date; set => SetProperty(ref _date, value); }
        public DateTime? Time { get => _time; set => SetProperty(ref _time, value); }
        public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
        public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
        public bool IsSaving { get => _isSaving; private set => SetProperty(ref _isSaving, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public DialogCloseListener RequestClose { get; }

        private async Task SaveAsync()
        {
            if (IsSaving) return;
            if (SelectedEmployee == null) { ErrorMessage = "Seleccione el empleado."; return; }
            if (Date == null || Time == null) { ErrorMessage = "Indique la fecha y la hora del marcaje."; return; }
            if (string.IsNullOrWhiteSpace(Reason)) { ErrorMessage = "Indique el motivo del marcaje manual."; return; }

            var checkTime = Date.Value.Date + Time.Value.TimeOfDay;
            if (checkTime > DateTime.Now) { ErrorMessage = "El marcaje no puede ser posterior a este momento."; return; }

            IsSaving = true;
            ErrorMessage = null;
            try
            {
                var result = await _mediator.Send(new RegisterManualAttendanceCommand(SelectedEmployee.Id, checkTime, Reason: Reason.Trim()));
                if (result.IsFailure)
                {
                    ErrorMessage = result.Error;
                    return;
                }

                // Como en la web: se recalcula el día del marcaje (el recálculo incluye la jornada previa si la alcanza).
                await _mediator.Send(new ProcessDailyAttendanceCommand(checkTime.Date, checkTime.Date, EmployeeId: EmployeeId.From(SelectedEmployee.Id)));
                RequestClose.Invoke(new DialogParameters { { "CheckTime", checkTime }, { "EmployeeName", SelectedEmployee.Display } }, ButtonResult.OK);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"No se pudo registrar el marcaje: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }

        public bool CanCloseDialog() => !IsSaving;
        public void OnDialogClosed() { }

        public async void OnDialogOpened(IDialogParameters parameters)
        {
            try
            {
                var result = await _mediator.Send(new GetAllEmployeesQuery());
                if (result.IsSuccess && result.Value != null)
                {
                    foreach (var employee in result.Value.Where(e => e.Status != EmployeeStatus.Baja).OrderBy(e => e.FullName))
                        Employees.Add(new EmployeeOption(employee.Id, $"{employee.Id} - {employee.FullName}"));
                }
                else
                {
                    ErrorMessage = $"No se pudieron cargar los empleados: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"No se pudieron cargar los empleados: {ex.Message}";
            }
        }
    }

    public sealed record EmployeeOption(string Id, string Display);
}
