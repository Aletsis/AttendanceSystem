using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using Microsoft.Extensions.Configuration;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport; 

namespace AttendanceSystem.WPF.ViewModels.Attendance
{
    public class AttendanceViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IReportExportService _reportExportService;
        private readonly IConfiguration _configuration;

        private ObservableCollection<AttendanceLogItem> _attendanceLogs = new();
        private AttendanceLogItem? _selectedLog;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private string _searchText = string.Empty;
        private List<AttendanceLogItem> _allLogsData = new();

        public ObservableCollection<AttendanceLogItem> AttendanceLogs { get => _attendanceLogs; set => SetProperty(ref _attendanceLogs, value); }
        public AttendanceLogItem? SelectedLog { get => _selectedLog; set => SetProperty(ref _selectedLog, value); }
        
        public DateTime StartDate 
        { 
            get => _startDate; 
            set { if (SetProperty(ref _startDate, value)) _ = LoadAttendanceLogsAsync(); } 
        }
        
        public DateTime EndDate 
        { 
            get => _endDate; 
            set { if (SetProperty(ref _endDate, value)) _ = LoadAttendanceLogsAsync(); } 
        }
        
        public string SearchText 
        { 
            get => _searchText; 
            set { if (SetProperty(ref _searchText, value)) FilterLogs(); } 
        }

        public ICommand ManualEntryCommand { get; }
        public ICommand CalculateAttendanceCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public AttendanceViewModel(
            IFrameNavigationService navigationService, 
            IMessageService messageService,
            IMediator mediator,
            IReportExportService reportExportService,
            IConfiguration configuration)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _reportExportService = reportExportService;
            _configuration = configuration;

            ManualEntryCommand = new DelegateCommand(ExecuteManualEntry);
            CalculateAttendanceCommand = new DelegateCommand(async () => await ExecuteCalculateAttendanceAsync());
            ExportCommand = new DelegateCommand(async () => await ExecuteExportAsync());
            RefreshCommand = new DelegateCommand(async () => await LoadAttendanceLogsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadAttendanceLogsAsync();
        }

        private async Task LoadAttendanceLogsAsync()
        {
            SetBusy(true, "Cargando registros de asistencia...");
            try
            {
                // 1. Get Employees for name mapping
                var employeesResult = await _mediator.Send(new GetAllEmployeesQuery());
                var employeeDict = new Dictionary<string, string>();
                
                if (employeesResult.IsSuccess && employeesResult.Value != null)
                {
                    employeeDict = employeesResult.Value.ToDictionary(e => e.Id, e => e.FullName);
                }

                // 2. Get Daily Attendance
                // Note: GetDailyAttendanceByDateRangeQuery takes optional BranchId and EmployeeId
                var query = new GetDailyAttendanceByDateRangeQuery(StartDate, EndDate);
                var attendanceResult = await _mediator.Send(query);
                
                _attendanceLogs.Clear();
                _allLogsData.Clear();

                // 3. Map to ViewModel items
                // Since handler returns List<DailyAttendance> (domain entities) directly, we map manually
                if (attendanceResult != null)
                {
                    foreach (var daily in attendanceResult)
                    {
                        var employeeId = daily.EmployeeId.Value;
                        var employeeName = employeeDict.TryGetValue(employeeId, out var name) ? name : "Desconocido";
                        
                        // Calculate status string
                        string status = "Asistencia";
                        if (daily.IsAbsent) status = "Falta";
                        else if (daily.IsRestDay && !daily.WorkedOnRestDay) status = "Descanso";
                        else if (daily.IsRestDay && daily.WorkedOnRestDay) status = "Descanso Laborado";
                        else if (daily.LateMinutes > 0) status = $"Retardo ({daily.LateMinutes}m)";
                        
                        var item = new AttendanceLogItem
                        {
                            Id = daily.Id.Value,
                            EmployeeIndex = employeeId, // Use as sorting key or similar
                            EmployeeNumber = employeeId,
                            EmployeeName = employeeName,
                            Date = daily.Date,
                            EntryTime = daily.ActualCheckIn?.ToString("HH:mm") ?? "--:--",
                            ExitTime = daily.ActualCheckOut?.ToString("HH:mm") ?? "--:--",
                            WorkedHours = CalculateWorkedHours(daily.ActualCheckIn, daily.ActualCheckOut),
                            Status = status,
                            Notes = daily.AttendanceNote
                        };
                        
                        _allLogsData.Add(item);
                    }
                    
                    FilterLogs();
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar registros: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }
        
        private string CalculateWorkedHours(DateTime? checkIn, DateTime? checkOut)
        {
             if (checkIn.HasValue && checkOut.HasValue)
             {
                 var diff = checkOut.Value - checkIn.Value;
                 return $"{(int)diff.TotalHours:00}:{diff.Minutes:00}";
             }
             return "--:--";
        }

        private void FilterLogs()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                AttendanceLogs = new ObservableCollection<AttendanceLogItem>(_allLogsData);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _allLogsData.Where(l => 
                    l.EmployeeName.ToLower().Contains(searchLower) || 
                    l.EmployeeNumber.ToLower().Contains(searchLower) ||
                    l.Status.ToLower().Contains(searchLower));
                    
                AttendanceLogs = new ObservableCollection<AttendanceLogItem>(filtered);
            }
        }

        private void ExecuteManualEntry()
        {
            _messageService.ShowMessageAsync("Entrada Manual", "Funcionalidad en desarrollo...");
        }

        private async Task ExecuteCalculateAttendanceAsync()
        {
            SetBusy(true, "Calculando asistencia...");
            try
            {
                var command = new ProcessDailyAttendanceCommand(StartDate, EndDate);
                var processedCount = await _mediator.Send(command);
                
                await _messageService.ShowSuccessAsync($"Cálculo de asistencia completado. Días procesados: {processedCount}");
                await LoadAttendanceLogsAsync();
            }
            catch (Exception ex) 
            { 
                await _messageService.ShowErrorAsync($"Error al calcular la asistencia: {ex.Message}"); 
            }
            finally 
            { 
                SetBusy(false); 
            }
        }

        private async Task ExecuteExportAsync()
        {
            SetBusy(true, "Exportando registros...");
            try
            {
                var query = new GetAttendanceReportQuery(StartDate, EndDate);
                var data = await _mediator.Send(query);

                if (data == null || !data.Any())
                {
                    await _messageService.ShowErrorAsync("No hay datos para exportar en el rango de fechas seleccionado.");
                    return;
                }

                var configResult = await _mediator.Send(new GetSystemConfigurationQuery());
                string companyName = "Sistema de Asistencia";
                byte[]? companyLogo = null;
                if (configResult.IsSuccess && configResult.Value != null)
                {
                    companyName = configResult.Value.CompanyName;
                    companyLogo = configResult.Value.CompanyLogo;
                }

                var excelBytes = _reportExportService.GenerateExcel(data, StartDate, EndDate, companyName, companyLogo, detailed: true);

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"Reporte_Asistencia_{StartDate:yyyyMMdd}_a_{EndDate:yyyyMMdd}.xlsx",
                    Title = "Guardar Reporte de Asistencia"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    await System.IO.File.WriteAllBytesAsync(saveFileDialog.FileName, excelBytes);
                    await _messageService.ShowSuccessAsync("Reporte exportado correctamente");
                }
            }
            catch (Exception ex) 
            { 
                await _messageService.ShowErrorAsync($"Error al exportar: {ex.Message}"); 
            }
            finally 
            { 
                SetBusy(false); 
            }
        }
    }

    public class AttendanceLogItem
    {
        public Guid Id { get; set; }
        public string EmployeeIndex { get; set; } = string.Empty; // For internal use
        public string EmployeeNumber { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? EntryTime { get; set; }
        public string? ExitTime { get; set; }
        public string? WorkedHours { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}
