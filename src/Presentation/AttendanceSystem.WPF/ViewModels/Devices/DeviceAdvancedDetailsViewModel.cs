using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.DownloadLogs.Queries.GetDownloadLogs;
using AttendanceSystem.Application.Features.Devices.Queries.GetDeviceUsers;
using AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;
using AttendanceSystem.Application.Features.Devices.Commands.ResetDeviceToFactory;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;
using AttendanceSystem.WPF.Services;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DeviceAdvancedDetailsViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IMessageService _messageService;
        private DeviceDto? _device;
        private bool _isLoadingHistory;
        private bool _isLoadingUsers;
        private DateTime? _historyFromDate;
        private DateTime? _historyToDate;
        private ObservableCollection<DownloadLog> _downloadHistory = new();
        private ObservableCollection<DeviceUserDto> _deviceUsers = new();

        public string Title => "Detalles Avanzados";
        public string Name => _device?.Name ?? "";
        public string IpAddress => _device?.IpAddress ?? "";
        public int Port => _device?.Port ?? 0;
        public string? Location => _device?.Location;
        public string Brand => _device?.Brand.ToString() ?? "";
        public string DownloadMethod => _device?.DownloadMethod.ToString() ?? "";
        public string? SerialNumber => _device?.SerialNumber;
        public string? FirmwareVersion => _device?.FirmwareVersion;
        public string ShouldClearAfterDownloadText => (_device?.ShouldClearAfterDownload ?? false) ? "Sí" : "No";
        public string StatusText => _device?.Status ?? "Desconectado";
        public Brush StatusColor => (_device?.IsActive ?? false) ? Brushes.Green : Brushes.Red;

        // Stats
        public int? UserCount => _device?.UserCount;
        public int? FingerprintCount => _device?.FingerprintCount;
        public int? FaceCount => _device?.FaceCount;
        public int? AttendanceRecordCount => _device?.AttendanceRecordCount;
        public int? UserCapacity => _device?.UserCapacity;
        public int? FingerprintCapacity => _device?.FingerprintCapacity;
        public int? FaceCapacity => _device?.FaceCapacity;
        public int? AttendanceRecordCapacity => _device?.AttendanceRecordCapacity;

        public bool IsLoadingHistory { get => _isLoadingHistory; set => SetProperty(ref _isLoadingHistory, value); }
        public bool IsLoadingUsers { get => _isLoadingUsers; set => SetProperty(ref _isLoadingUsers, value); }
        public DateTime? HistoryFromDate { get => _historyFromDate; set => SetProperty(ref _historyFromDate, value); }
        public DateTime? HistoryToDate { get => _historyToDate; set => SetProperty(ref _historyToDate, value); }
        public ObservableCollection<DownloadLog> DownloadHistory { get => _downloadHistory; set => SetProperty(ref _downloadHistory, value); }
        public ObservableCollection<DeviceUserDto> DeviceUsers { get => _deviceUsers; set => SetProperty(ref _deviceUsers, value); }

        public ICommand FilterHistoryCommand { get; }
        public ICommand ShowLogDetailsCommand { get; }
        public ICommand LoadUsersCommand { get; }
        public ICommand SetTimeCommand { get; }
        public ICommand ResetToFactoryCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<IDialogResult>? RequestClose;

        public DeviceAdvancedDetailsViewModel(
            IMediator mediator, 
            IDialogService dialogService,
            IMessageService messageService)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            _messageService = messageService;

            FilterHistoryCommand = new DelegateCommand(async () => await LoadHistoryAsync());
            ShowLogDetailsCommand = new DelegateCommand<DownloadLog>(ExecuteShowLogDetails);
            LoadUsersCommand = new DelegateCommand(async () => await LoadUsersAsync());
            SetTimeCommand = new DelegateCommand(async () => await ExecuteSetTimeAsync());
            ResetToFactoryCommand = new DelegateCommand(async () => await ExecuteResetToFactoryAsync());
            ClearLogsCommand = new DelegateCommand(async () => await ExecuteClearLogsAsync());
            CancelCommand = new DelegateCommand(() => RequestClose?.Invoke(new DialogResult(ButtonResult.OK)));
        }

        private async Task LoadHistoryAsync()
        {
            if (_device == null) return;
            IsLoadingHistory = true;
            try
            {
                DateTime? toDateAdjusted = HistoryToDate?.AddDays(1).AddTicks(-1);
                var result = await _mediator.Send(new GetDownloadLogsQuery(HistoryFromDate, toDateAdjusted));
                if (result.IsSuccess)
                {
                    var filtered = result.Value.Where(l => l.DeviceId.Value == _device.DeviceId).OrderByDescending(l => l.StartedAt);
                    DownloadHistory = new ObservableCollection<DownloadLog>(filtered);
                }
            }
            finally
            {
                IsLoadingHistory = false;
            }
        }

        private async Task LoadUsersAsync()
        {
            if (_device == null) return;
            IsLoadingUsers = true;
            try
            {
                var result = await _mediator.Send(new GetDeviceUsersQuery(_device.DeviceId));
                if (result.IsSuccess)
                {
                    DeviceUsers = new ObservableCollection<DeviceUserDto>(result.Value);
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar usuarios: {result.Error}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { IsLoadingUsers = false; }
        }

        private async Task ExecuteSetTimeAsync()
        {
            if (_device == null) return;
            var confirmed = await _messageService.ShowConfirmationAsync("Sincronizar Hora", "¿Desea sincronizar la fecha y hora del dispositivo con este servidor?");
            if (!confirmed) return;

            try
            {
                var result = await _mediator.Send(new SetDeviceTimeCommand(_device.DeviceId, DateTime.Now));
                if (result.IsSuccess) await _messageService.ShowSuccessAsync("Hora sincronizada correctamente.");
                else await _messageService.ShowErrorAsync($"Error: {result.Error}");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
        }

        private async Task ExecuteResetToFactoryAsync()
        {
            if (_device == null) return;
            var confirmed = await _messageService.ShowConfirmationAsync("Reiniciar Fábrica", "¡ADVERTENCIA! Esta acción borrará toda la configuración del dispositivo. ¿Desea continuar?");
            if (!confirmed) return;

            try
            {
                var result = await _mediator.Send(new ResetDeviceToFactoryCommand(_device.DeviceId));
                if (result.IsSuccess) await _messageService.ShowSuccessAsync("Comando de reinicio enviado.");
                else await _messageService.ShowErrorAsync($"Error: {result.Error}");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
        }

        private async Task ExecuteClearLogsAsync()
        {
            await _messageService.ShowMessageAsync("No Implementado", "La funcionalidad de borrado manual de logs está en desarrollo.");
        }

        private void ExecuteShowLogDetails(DownloadLog log)
        {
            var parameters = new DialogParameters
            {
                { "Log", log },
                { "DeviceName", Name }
            };
            _dialogService.ShowDialog("DownloadLogDetailsDialog", parameters, null);
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Device"))
            {
                _device = parameters.GetValue<DeviceDto>("Device");
                RaisePropertyChanged(string.Empty);
                _ = LoadHistoryAsync();
            }
        }
    }
}
