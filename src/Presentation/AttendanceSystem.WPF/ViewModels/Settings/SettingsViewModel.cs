using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Configuration.Commands.UpdateSystemConfiguration;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.WPF.ViewModels.Settings
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private SystemConfigurationDto? _currentConfig;

        // General Settings
        private string _companyName = string.Empty;
        private int _toleranceMinutes = 15;
        private int _maxOvertimeHours = 4;
        
        // Email Settings
        private string _emailServer = string.Empty;
        private int _emailPort = 587;
        private string _emailUsername = string.Empty;
        private string _emailPassword = string.Empty;
        private bool _emailUseSsl = true;
        
        // ADMS Settings
        private int _admsPort = 5005;
        private bool _admsAutoStart = true;

        public string CompanyName { get => _companyName; set => SetProperty(ref _companyName, value); }
        public int ToleranceMinutes { get => _toleranceMinutes; set => SetProperty(ref _toleranceMinutes, value); }
        public int MaxOvertimeHours { get => _maxOvertimeHours; set => SetProperty(ref _maxOvertimeHours, value); }
        public string EmailServer { get => _emailServer; set => SetProperty(ref _emailServer, value); }
        public int EmailPort { get => _emailPort; set => SetProperty(ref _emailPort, value); }
        public string EmailUsername { get => _emailUsername; set => SetProperty(ref _emailUsername, value); }
        public string EmailPassword { get => _emailPassword; set => SetProperty(ref _emailPassword, value); }
        public bool EmailUseSsl { get => _emailUseSsl; set => SetProperty(ref _emailUseSsl, value); }
        public int AdmsPort { get => _admsPort; set => SetProperty(ref _admsPort, value); }
        public bool AdmsAutoStart { get => _admsAutoStart; set => SetProperty(ref _admsAutoStart, value); }

        public ICommand SaveSettingsCommand { get; }
        public ICommand TestEmailCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public SettingsViewModel(
            IFrameNavigationService navigationService, 
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            SaveSettingsCommand = new DelegateCommand(async () => await ExecuteSaveSettingsAsync());
            TestEmailCommand = new DelegateCommand(async () => await ExecuteTestEmailAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadSettingsAsync();
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
                    ToleranceMinutes = (int)_currentConfig.LateToleranceMinutes.TotalMinutes;
                    AdmsPort = _currentConfig.AdmsPort;
                    AdmsAutoStart = _currentConfig.IsAutoDownloadEnabled;

                    // Email settings are not yet fully supported by backend DTO, keeping defaults or local storage if needed
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
                if (_currentConfig == null)
                {
                    await _messageService.ShowErrorAsync("No hay configuración base cargada.");
                    return;
                }

                var command = new UpdateSystemConfigurationCommand(
                    CompanyName,
                    _currentConfig.CompanyLogo,
                    TimeSpan.FromMinutes(ToleranceMinutes),
                    _currentConfig.StandardWorkHours,
                    _currentConfig.AutoClearDevicesAfterDownload,
                    _currentConfig.SendEmailAlerts,
                    _currentConfig.AlertEmailRecipient,
                    AdmsAutoStart,
                    _currentConfig.AutoDownloadTime,
                    _currentConfig.AutoDownloadOnlyToday,
                    AdmsPort,
                    _currentConfig.BackupDirectory,
                    _currentConfig.BackupTimeoutMinutes,
                    _currentConfig.WorkPeriodMode,
                    _currentConfig.WeeklyStartDay,
                    _currentConfig.FortnightFirstDay,
                    _currentConfig.FortnightSecondDay,
                    _currentConfig.MonthlyStartDay
                );

                var result = await _mediator.Send(command);

                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Configuración guardada correctamente");
                    // Reload to ensure sync
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

        private async Task ExecuteTestEmailAsync()
        {
            SetBusy(true, "Probando configuración de correo...");
            try
            {
                await Task.Delay(1000);
                await _messageService.ShowSuccessAsync("Simulación: Correo de prueba enviado (Configuración SMTP no persistida en Backend aún)");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error al enviar correo: {ex.Message}"); }
            finally { SetBusy(false); }
        }
    }
}
