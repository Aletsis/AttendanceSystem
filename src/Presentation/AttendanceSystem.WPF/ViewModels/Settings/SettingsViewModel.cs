using System;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Configuration.Commands.UpdateSystemConfiguration;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Win32;
using System.IO;

using Microsoft.Extensions.Configuration;

namespace AttendanceSystem.WPF.ViewModels.Settings
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IConfiguration _configuration;
        private readonly Prism.Dialogs.IDialogService? _dialogService;

        private SystemConfigurationDto? _currentConfig;

        // Identity
        private string _companyName = string.Empty;
        private byte[]? _companyLogo;

        // General Settings
        private int _toleranceMinutes = 15;
        private double _standardWorkHours = 8;

        // ADMS Settings
        private int _admsPort = 5005;
        private bool _isAutoDownloadEnabled = true;
        private bool _autoDownloadOnlyToday = true;
        private TimeSpan? _autoDownloadTime = new TimeSpan(23, 0, 0);

        // Backup Settings
        private string _backupDirectory = "Backups";
        private int _backupTimeoutMinutes = 10;

        // Work Period Settings
        private WorkPeriodMode _workPeriodMode = WorkPeriodMode.Weekly;
        private DayOfWeek _weeklyStartDay = DayOfWeek.Monday;
        private int _fortnightFirstDay = 1;
        private int _fortnightSecondDay = 16;
        private int _monthlyStartDay = 1;

        private bool _autoClearDevicesAfterDownload;

        // Comida y marcajes (afectan el cálculo)
        private bool _lunchWindowEnabled = true;
        private TimeSpan? _lunchWindowStart = new(13, 0, 0);
        private TimeSpan? _lunchWindowEnd = new(16, 0, 0);
        private int _paidLunchMinutes = 30;
        private TimeSpan? _nightLunchWindowStart = new(23, 0, 0);
        private TimeSpan? _nightLunchWindowEnd = new(4, 0, 0);
        private int _doubleTapMinutes = 15;

        // Respaldos y reporte automáticos
        private bool _isAutoBackupEnabled;
        private TimeSpan? _autoBackupTime = new(2, 0, 0);
        private bool _isAutoReportEnabled;
        private TimeSpan? _autoReportTime = new(9, 0, 0);
        private string? _autoReportEmails;
        private bool _autoReportForToday;

        // Alertas y correo
        private bool _areAlertsEnabled;
        private string? _absenceAlertEmails;
        private string? _lateAlertEmails;
        private string? _systemFailureAlertEmails;
        private string? _smtpHost;
        private int _smtpPort = 587;
        private string? _smtpUser;
        private string? _smtpPassword;
        private bool _smtpEnableSsl = true;

        public string CompanyName { get => _companyName; set => SetProperty(ref _companyName, value); }
        public byte[]? CompanyLogo
        {
            get => _companyLogo;
            set
            {
                if (SetProperty(ref _companyLogo, value)) RaisePropertyChanged(nameof(HasLogo));
            }
        }
        public int ToleranceMinutes { get => _toleranceMinutes; set => SetProperty(ref _toleranceMinutes, value); }
        /// <summary>Horas de la jornada; admite fracciones (7.5) que antes se truncaban al guardar.</summary>
        public double StandardWorkHours { get => _standardWorkHours; set => SetProperty(ref _standardWorkHours, value); }
        public int AdmsPort { get => _admsPort; set => SetProperty(ref _admsPort, value); }
        public bool IsAutoDownloadEnabled { get => _isAutoDownloadEnabled; set => SetProperty(ref _isAutoDownloadEnabled, value); }
        public bool AutoDownloadOnlyToday { get => _autoDownloadOnlyToday; set => SetProperty(ref _autoDownloadOnlyToday, value); }
        public TimeSpan? AutoDownloadTime { get => _autoDownloadTime; set => SetProperty(ref _autoDownloadTime, value); }
        public string BackupDirectory { get => _backupDirectory; set => SetProperty(ref _backupDirectory, value); }
        public int BackupTimeoutMinutes { get => _backupTimeoutMinutes; set => SetProperty(ref _backupTimeoutMinutes, value); }
        public bool AutoClearDevicesAfterDownload { get => _autoClearDevicesAfterDownload; set => SetProperty(ref _autoClearDevicesAfterDownload, value); }

        public bool LunchWindowEnabled { get => _lunchWindowEnabled; set => SetProperty(ref _lunchWindowEnabled, value); }
        public DateTime? LunchWindowStart { get => ToDateTime(_lunchWindowStart); set => SetTime(ref _lunchWindowStart, value); }
        public DateTime? LunchWindowEnd { get => ToDateTime(_lunchWindowEnd); set => SetTime(ref _lunchWindowEnd, value); }
        public int PaidLunchMinutes { get => _paidLunchMinutes; set => SetProperty(ref _paidLunchMinutes, value); }
        public DateTime? NightLunchWindowStart { get => ToDateTime(_nightLunchWindowStart); set => SetTime(ref _nightLunchWindowStart, value); }
        public DateTime? NightLunchWindowEnd { get => ToDateTime(_nightLunchWindowEnd); set => SetTime(ref _nightLunchWindowEnd, value); }
        public int DoubleTapMinutes { get => _doubleTapMinutes; set => SetProperty(ref _doubleTapMinutes, value); }

        public bool IsAutoBackupEnabled { get => _isAutoBackupEnabled; set => SetProperty(ref _isAutoBackupEnabled, value); }
        public DateTime? AutoBackupTime { get => ToDateTime(_autoBackupTime); set => SetTime(ref _autoBackupTime, value); }
        public bool IsAutoReportEnabled { get => _isAutoReportEnabled; set => SetProperty(ref _isAutoReportEnabled, value); }
        public DateTime? AutoReportTime { get => ToDateTime(_autoReportTime); set => SetTime(ref _autoReportTime, value); }
        public string? AutoReportEmails { get => _autoReportEmails; set => SetProperty(ref _autoReportEmails, value); }
        public bool AutoReportForToday { get => _autoReportForToday; set => SetProperty(ref _autoReportForToday, value); }

        public bool AreAlertsEnabled { get => _areAlertsEnabled; set => SetProperty(ref _areAlertsEnabled, value); }
        public string? AbsenceAlertEmails { get => _absenceAlertEmails; set => SetProperty(ref _absenceAlertEmails, value); }
        public string? LateAlertEmails { get => _lateAlertEmails; set => SetProperty(ref _lateAlertEmails, value); }
        public string? SystemFailureAlertEmails { get => _systemFailureAlertEmails; set => SetProperty(ref _systemFailureAlertEmails, value); }
        public string? SmtpHost { get => _smtpHost; set => SetProperty(ref _smtpHost, value); }
        public int SmtpPort { get => _smtpPort; set => SetProperty(ref _smtpPort, value); }
        public string? SmtpUser { get => _smtpUser; set => SetProperty(ref _smtpUser, value); }
        public string? SmtpPassword { get => _smtpPassword; set => SetProperty(ref _smtpPassword, value); }
        public bool SmtpEnableSsl { get => _smtpEnableSsl; set => SetProperty(ref _smtpEnableSsl, value); }

        public bool HasLogo => CompanyLogo is { Length: > 0 };

        /// <summary>Los selectores de hora trabajan con DateTime; la configuración guarda la hora del día.</summary>
        private static DateTime? ToDateTime(TimeSpan? time) => time.HasValue ? DateTime.Today.Add(time.Value) : null;

        private void SetTime(ref TimeSpan? field, DateTime? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            var time = value?.TimeOfDay;
            if (field == time) return;
            field = time;
            RaisePropertyChanged(propertyName);
        }

        public DateTime? AutoDownloadDateTime
        {
            get => AutoDownloadTime.HasValue ? DateTime.Today.Add(AutoDownloadTime.Value) : null;
            set
            {
                if (value.HasValue) AutoDownloadTime = value.Value.TimeOfDay;
                else AutoDownloadTime = null;
                RaisePropertyChanged(nameof(AutoDownloadDateTime));
            }
        }

        // Work Period Properties
        public WorkPeriodMode WorkPeriodMode { get => _workPeriodMode; set => SetProperty(ref _workPeriodMode, value); }
        public DayOfWeek WeeklyStartDay { get => _weeklyStartDay; set => SetProperty(ref _weeklyStartDay, value); }
        public int FortnightFirstDay { get => _fortnightFirstDay; set => SetProperty(ref _fortnightFirstDay, value); }
        public int FortnightSecondDay { get => _fortnightSecondDay; set => SetProperty(ref _fortnightSecondDay, value); }
        public int MonthlyStartDay { get => _monthlyStartDay; set => SetProperty(ref _monthlyStartDay, value); }

        public IEnumerable<WorkPeriodMode> WorkPeriodModes => Enum.GetValues(typeof(WorkPeriodMode)).Cast<WorkPeriodMode>();
        public IEnumerable<DayOfWeek> DaysOfWeek => Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>();

        public ICommand SaveSettingsCommand { get; }
        public ICommand UploadLogoCommand { get; }
        public ICommand RemoveLogoCommand { get; }
        public ICommand SelectBackupFolderCommand { get; }

        public SettingsViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator,
            IConfiguration configuration,
            Prism.Dialogs.IDialogService? dialogService = null)
        {
            _dialogService = dialogService;
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _configuration = configuration;

            SaveSettingsCommand = new DelegateCommand(async () => await ExecuteSaveSettingsAsync());
            UploadLogoCommand = new DelegateCommand(async () => await ExecuteUploadLogoAsync());
            RemoveLogoCommand = new DelegateCommand(() => CompanyLogo = null);
            SelectBackupFolderCommand = new DelegateCommand(ExecuteSelectBackupFolder);

            _ = LoadSettingsAsync();
        }

        private void ExecuteSelectBackupFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Seleccionar Carpeta de Respaldos",
                InitialDirectory = string.IsNullOrEmpty(BackupDirectory) ? AppDomain.CurrentDomain.BaseDirectory : BackupDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                BackupDirectory = dialog.FolderName;
            }
        }

        private async Task LoadSettingsAsync()
        {
            SetBusy(true, "Cargando configuración...");
            try
            {
                var result = await _mediator.Send(new GetSystemConfigurationQuery());
                if (result.IsSuccess && result.Value != null)
                {
                    _currentConfig = result.Value;

                    CompanyName = _currentConfig.CompanyName;
                    CompanyLogo = _currentConfig.CompanyLogo;
                    ToleranceMinutes = (int)_currentConfig.LateToleranceMinutes.TotalMinutes;
                    StandardWorkHours = _currentConfig.StandardWorkHours.TotalHours;
                    AdmsPort = _currentConfig.AdmsPort;
                    IsAutoDownloadEnabled = _currentConfig.IsAutoDownloadEnabled;
                    AutoDownloadTime = _currentConfig.AutoDownloadTime;
                    AutoDownloadOnlyToday = _currentConfig.AutoDownloadOnlyToday;
                    BackupDirectory = _currentConfig.BackupDirectory;
                    BackupTimeoutMinutes = _currentConfig.BackupTimeoutMinutes;
                    AutoClearDevicesAfterDownload = _currentConfig.AutoClearDevicesAfterDownload;

                    WorkPeriodMode = _currentConfig.WorkPeriodMode;
                    WeeklyStartDay = _currentConfig.WeeklyStartDay;
                    FortnightFirstDay = _currentConfig.FortnightFirstDay;
                    FortnightSecondDay = _currentConfig.FortnightSecondDay;
                    MonthlyStartDay = _currentConfig.MonthlyStartDay;

                    LunchWindowEnabled = _currentConfig.LunchWindowEnabled;
                    if (_currentConfig.LunchWindowStart.HasValue) LunchWindowStart = ToDateTime(_currentConfig.LunchWindowStart);
                    if (_currentConfig.LunchWindowEnd.HasValue) LunchWindowEnd = ToDateTime(_currentConfig.LunchWindowEnd);
                    PaidLunchMinutes = _currentConfig.PaidLunchMinutes;
                    if (_currentConfig.NightLunchWindowStart.HasValue) NightLunchWindowStart = ToDateTime(_currentConfig.NightLunchWindowStart);
                    if (_currentConfig.NightLunchWindowEnd.HasValue) NightLunchWindowEnd = ToDateTime(_currentConfig.NightLunchWindowEnd);
                    DoubleTapMinutes = _currentConfig.DoubleTapMinutes;

                    IsAutoBackupEnabled = _currentConfig.IsAutoBackupEnabled;
                    if (_currentConfig.AutoBackupTime.HasValue) AutoBackupTime = ToDateTime(_currentConfig.AutoBackupTime);
                    IsAutoReportEnabled = _currentConfig.IsAutoReportEnabled;
                    if (_currentConfig.AutoReportTime.HasValue) AutoReportTime = ToDateTime(_currentConfig.AutoReportTime);
                    AutoReportEmails = _currentConfig.AutoReportEmails;
                    AutoReportForToday = _currentConfig.AutoReportForToday;

                    AreAlertsEnabled = _currentConfig.AreAlertsEnabled;
                    AbsenceAlertEmails = _currentConfig.AbsenceAlertEmails;
                    LateAlertEmails = _currentConfig.LateAlertEmails;
                    SystemFailureAlertEmails = _currentConfig.SystemFailureAlertEmails;
                    SmtpHost = _currentConfig.SmtpHost;
                    SmtpPort = _currentConfig.SmtpPort;
                    SmtpUser = _currentConfig.SmtpUser;
                    SmtpPassword = _currentConfig.SmtpPassword;
                    SmtpEnableSsl = _currentConfig.SmtpEnableSsl;

                    _loadedCalculation = CalculationSettings();
                }
                else
                {
                    await _messageService.ShowErrorAsync("No se pudo cargar la configuración.");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        // Caso 15 (como la web): periodos, comida y toque doble cambian el cálculo; si cambian se pregunta desde cuándo aplican.
        private string? _loadedCalculation;

        private string CalculationSettings() => string.Join("|", WorkPeriodMode, WeeklyStartDay, FortnightFirstDay, FortnightSecondDay, MonthlyStartDay,
            _lunchWindowStart, _lunchWindowEnd, PaidLunchMinutes, DoubleTapMinutes, LunchWindowEnabled, _nightLunchWindowStart, _nightLunchWindowEnd);

        private async Task ExecuteSaveSettingsAsync()
        {
            // Sin la configuración cargada se guardarían valores por omisión en lo que esta pantalla no edita
            // (correo, alertas, respaldos y reportes automáticos, nube).
            if (_currentConfig == null)
            {
                await _messageService.ShowErrorAsync("No se pudo cargar la configuración actual, así que no se puede guardar. Vuelva a abrir la pantalla e intente de nuevo.");
                return;
            }

            DateTime? recalculateFrom = null;
            if (_dialogService != null && _loadedCalculation != null && CalculationSettings() != _loadedCalculation)
            {
                recalculateFrom = await _dialogService.AskEffectiveDateAsync("Aplicar cambios del cálculo",
                    "Cambió la comida, el toque doble o los periodos. ¿Desde qué fecha aplican?",
                    date => _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Consolidation.GetConfigurationRecalculationImpactQuery(date)));
                if (recalculateFrom == null) return;
            }

            SetBusy(true, "Guardando configuración...");
            try
            {
                var command = new UpdateSystemConfigurationCommand(
                    CompanyName,
                    CompanyLogo,
                    TimeSpan.FromMinutes(ToleranceMinutes),
                    TimeSpan.FromMinutes(Math.Round(StandardWorkHours * 60)),
                    AutoClearDevicesAfterDownload,
                    IsAutoDownloadEnabled,
                    AutoDownloadTime,
                    AutoDownloadOnlyToday,
                    AdmsPort,
                    BackupDirectory,
                    BackupTimeoutMinutes,
                    WorkPeriodMode,
                    WeeklyStartDay,
                    FortnightFirstDay,
                    FortnightSecondDay,
                    MonthlyStartDay,
                    AreAlertsEnabled,
                    AbsenceAlertEmails,
                    LateAlertEmails,
                    SystemFailureAlertEmails,
                    SmtpHost,
                    SmtpPort,
                    SmtpUser,
                    SmtpPassword,
                    SmtpEnableSsl,
                    IsAutoBackupEnabled,
                    _autoBackupTime,
                    IsAutoReportEnabled,
                    _autoReportTime,
                    AutoReportEmails,
                    AutoReportForToday,
                    // La nube y la cuarentena se configuran en la web: se conserva lo guardado (la cuarentena nula no cambia).
                    _currentConfig.IsCloudSyncEnabled,
                    _currentConfig.CloudDbConnectionString,
                    _lunchWindowStart,
                    _lunchWindowEnd,
                    PaidLunchMinutes,
                    _nightLunchWindowStart,
                    _nightLunchWindowEnd,
                    LunchWindowEnabled,
                    DoubleTapMinutes,
                    RecalculateFrom: recalculateFrom
                );

                var result = await _mediator.Send(command);

                if (result.IsSuccess)
                {
                    // Notificar al backend Blazor Server para recargar las tareas Hangfire en segundo plano
                    string? reloadProblem = null;
                    try
                    {
                        var backendUrl = _configuration["BackendUrl"] ?? "http://localhost:18372";
                        using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                        var apiKey = _configuration["ApiKey"] ?? _configuration["ZKTecoService:ApiKey"];
                        if (!string.IsNullOrWhiteSpace(apiKey))
                        {
                            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                        }
                        var reloadResponse = await httpClient.PostAsync($"{backendUrl}/api/configuration/reload-jobs", null);
                        if (!reloadResponse.IsSuccessStatusCode)
                        {
                            reloadProblem = $"respondió {(int)reloadResponse.StatusCode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        reloadProblem = ex.Message;
                    }

                    if (reloadProblem != null)
                    {
                        // Antes sólo se escribía en la depuración: el usuario creía aplicados los horarios automáticos.
                        Serilog.Log.Warning("El servidor web no recargó las tareas programadas: {Problem}", reloadProblem);
                        await _messageService.ShowWarningAsync("La configuración se guardó, pero el servidor web no confirmó la recarga de las tareas programadas " +
                            $"({reloadProblem}). Las descargas, respaldos y reportes automáticos usarán el horario nuevo cuando se reinicie el sitio web.");
                        await LoadSettingsAsync();
                        return;
                    }

                    await _messageService.ShowSuccessAsync("Configuración guardada correctamente");
                    await LoadSettingsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al guardar: {result.Error}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteUploadLogoAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Imágenes (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Seleccionar Logo de la Empresa"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > 2 * 1024 * 1024)
                    {
                        await _messageService.ShowErrorAsync("El archivo es demasiado grande (Máximo 2MB)");
                        return;
                    }
                    CompanyLogo = await File.ReadAllBytesAsync(openFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    await _messageService.ShowErrorAsync($"Error al cargar la imagen: {ex.Message}");
                }
            }
        }
    }
}
