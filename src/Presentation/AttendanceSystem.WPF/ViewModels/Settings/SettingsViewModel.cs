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

        private SystemConfigurationDto? _currentConfig;

        // Identity
        private string _companyName = string.Empty;
        private byte[]? _companyLogo;

        // General Settings
        private int _toleranceMinutes = 15;
        private int _standardWorkHours = 8;
        
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

        public string CompanyName { get => _companyName; set => SetProperty(ref _companyName, value); }
        public byte[]? CompanyLogo { get => _companyLogo; set => SetProperty(ref _companyLogo, value); }
        public int ToleranceMinutes { get => _toleranceMinutes; set => SetProperty(ref _toleranceMinutes, value); }
        public int StandardWorkHours { get => _standardWorkHours; set => SetProperty(ref _standardWorkHours, value); }
        public int AdmsPort { get => _admsPort; set => SetProperty(ref _admsPort, value); }
        public bool IsAutoDownloadEnabled { get => _isAutoDownloadEnabled; set => SetProperty(ref _isAutoDownloadEnabled, value); }
        public bool AutoDownloadOnlyToday { get => _autoDownloadOnlyToday; set => SetProperty(ref _autoDownloadOnlyToday, value); }
        public TimeSpan? AutoDownloadTime { get => _autoDownloadTime; set => SetProperty(ref _autoDownloadTime, value); }
        public string BackupDirectory { get => _backupDirectory; set => SetProperty(ref _backupDirectory, value); }
        public int BackupTimeoutMinutes { get => _backupTimeoutMinutes; set => SetProperty(ref _backupTimeoutMinutes, value); }
        public bool AutoClearDevicesAfterDownload { get => _autoClearDevicesAfterDownload; set => SetProperty(ref _autoClearDevicesAfterDownload, value); }

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
        public ICommand BackToDashboardCommand { get; }
        public ICommand UploadLogoCommand { get; }
        public ICommand RemoveLogoCommand { get; }
        public ICommand SelectBackupFolderCommand { get; }

        public SettingsViewModel(
            IFrameNavigationService navigationService, 
            IMessageService messageService,
            IMediator mediator,
            IConfiguration configuration)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _configuration = configuration;

            SaveSettingsCommand = new DelegateCommand(async () => await ExecuteSaveSettingsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());
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
                    StandardWorkHours = (int)_currentConfig.StandardWorkHours.TotalHours;
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
                }
                else
                {
                    await _messageService.ShowErrorAsync("No se pudo cargar la configuración.");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteSaveSettingsAsync()
        {
            SetBusy(true, "Guardando configuración...");
            try
            {
                var command = new UpdateSystemConfigurationCommand(
                    CompanyName,
                    CompanyLogo,
                    TimeSpan.FromMinutes(ToleranceMinutes),
                    TimeSpan.FromHours(StandardWorkHours),
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
                    _currentConfig?.AreAlertsEnabled ?? false,
                    _currentConfig?.AbsenceAlertEmails,
                    _currentConfig?.LateAlertEmails,
                    _currentConfig?.SystemFailureAlertEmails,
                    _currentConfig?.SmtpHost,
                    _currentConfig?.SmtpPort ?? 587,
                    _currentConfig?.SmtpUser,
                    _currentConfig?.SmtpPassword,
                    _currentConfig?.SmtpEnableSsl ?? true,
                    _currentConfig?.IsAutoBackupEnabled ?? false,
                    _currentConfig?.AutoBackupTime,
                    _currentConfig?.IsAutoReportEnabled ?? false,
                    _currentConfig?.AutoReportTime,
                    _currentConfig?.AutoReportEmails
                );

                var result = await _mediator.Send(command);

                if (result.IsSuccess)
                {
                    // Notificar al backend Blazor Server para recargar las tareas Hangfire en segundo plano
                    try
                    {
                        var backendUrl = _configuration["BackendUrl"] ?? "http://localhost:18372";
                        using var httpClient = new System.Net.Http.HttpClient();
                        var reloadResponse = await httpClient.PostAsync($"{backendUrl}/api/configuration/reload-jobs", null);
                        if (!reloadResponse.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error notifying backend of configuration change: {reloadResponse.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error notifying backend: {ex.Message}");
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
