using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using AttendanceSystem.Application.Common;
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
using Prism.Dialogs;
using AttendanceSystem.WPF.Services;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DeviceAdvancedDetailsViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IMessageService _messageService;
        private readonly IAuthenticationStateService _authService;
        private DeviceDto? _device;
        private bool _isLoadingHistory;
        private bool _isLoadingUsers;
        private DateTime? _historyFromDate;
        private DateTime? _historyToDate;
        private ObservableCollection<DownloadLog> _downloadHistory = new();
        private ObservableCollection<DeviceUserDto> _deviceUsers = new();

        public string Title => "Detalles del reloj";
        public string Name => _device?.Name ?? "";
        public string IpAddress => _device?.IpAddress ?? "";
        public int Port => _device?.Port ?? 0;
        public string Address => _device == null ? "" : $"{_device.IpAddress}:{_device.Port}";
        public string? Location => _device?.Location;
        public string Brand => _device?.Brand.ToString() ?? "";
        public string DownloadMethod => _device == null ? "" : Converters.EnumDisplay.Text(_device.DownloadMethod);
        public string? SerialNumber => _device?.SerialNumber;
        public string? FirmwareVersion => _device?.FirmwareVersion;
        public string? Platform => _device?.Platform;
        public string? MacAddress => _device?.MacAddress;
        public string ShouldClearAfterDownloadText => (_device?.ShouldClearAfterDownload ?? false) ? "Sí, tras cada descarga completa" : "No";
        public string TimeZoneText => string.IsNullOrWhiteSpace(_device?.TimeZoneId) ? "La del sistema" : TimeZoneCatalog.Describe(_device!.TimeZoneId!);
        public string LastDownloadText => _device?.LastDownloadAt is { } last ? last.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Nunca";
        public string LastActivityText => _device?.LastActivityAt is { } activity ? activity.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Sin registro";
        public int TotalDownloadCount => _device?.TotalDownloadCount ?? 0;

        /// <summary>Estado real del reloj: un reloj dado de baja se muestra inactivo aunque su último estado fuera otro.</summary>
        public string StatusText => _device == null ? "" : !_device.IsActive ? "Inactivo" : _device.Status;
        /// <summary>online, offline, error o inactive: colorea la ficha del estado.</summary>
        public string StatusKind => _device == null ? "offline"
            : !_device.IsActive ? "inactive"
            : _device.Status == "En Línea" || _device.Status == "Online" ? "online"
            : _device.Status == "Error" ? "error"
            : "offline";

        /// <summary>Memoria del reloj: lo ocupado contra la capacidad que reporta el equipo.</summary>
        public IReadOnlyList<CapacityItem> Capacities => _device == null ? Array.Empty<CapacityItem>() : new[]
        {
            new CapacityItem("Checadas", "History", _device.AttendanceRecordCount, _device.AttendanceRecordCapacity),
            new CapacityItem("Usuarios", "AccountGroup", _device.UserCount, _device.UserCapacity),
            new CapacityItem("Huellas", "Fingerprint", _device.FingerprintCount, _device.FingerprintCapacity),
            new CapacityItem("Rostros", "FaceRecognition", _device.FaceCount, _device.FaceCapacity)
        };

        public bool IsAdms => _device?.DownloadMethod == DeviceDownloadMethod.Adms;
        public string UsersSummary => DeviceUsers.Count == 0 ? "Consulte el reloj para ver sus usuarios." : $"{DeviceUsers.Count} usuario(s) en el reloj.";

        private bool _isWorking;
        private string _workingMessage = string.Empty;
        /// <summary>Una acción sobre el reloj en curso (ajustar hora, reiniciar): se bloquea la ventana mientras responde.</summary>
        public bool IsWorking { get => _isWorking; private set => SetProperty(ref _isWorking, value); }
        public string WorkingMessage { get => _workingMessage; private set => SetProperty(ref _workingMessage, value); }

        public bool IsLoadingHistory { get => _isLoadingHistory; set => SetProperty(ref _isLoadingHistory, value); }
        public bool IsLoadingUsers { get => _isLoadingUsers; set => SetProperty(ref _isLoadingUsers, value); }
        public DateTime? HistoryFromDate { get => _historyFromDate; set => SetProperty(ref _historyFromDate, value); }
        public DateTime? HistoryToDate { get => _historyToDate; set => SetProperty(ref _historyToDate, value); }
        public ObservableCollection<DownloadLog> DownloadHistory { get => _downloadHistory; set => SetProperty(ref _downloadHistory, value); }
        public ObservableCollection<DeviceUserDto> DeviceUsers
        {
            get => _deviceUsers;
            set
            {
                if (SetProperty(ref _deviceUsers, value)) RaisePropertyChanged(nameof(UsersSummary));
            }
        }

        /// <summary>Caso 13: ver usuarios, ajustar la hora, vaciar o reiniciar el reloj es de administración de dispositivos.</summary>
        public bool CanManage => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.DevicesManage);

        public ICommand FilterHistoryCommand { get; }
        public ICommand ShowLogDetailsCommand { get; }
        public ICommand LoadUsersCommand { get; }
        public ICommand SetTimeCommand { get; }
        public ICommand ResetToFactoryCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; }

        public DeviceAdvancedDetailsViewModel(
            IMediator mediator,
            IDialogService dialogService,
            IMessageService messageService,
            IAuthenticationStateService authService)
        {
            _authService = authService;
            _mediator = mediator;
            _dialogService = dialogService;
            _messageService = messageService;

            FilterHistoryCommand = new DelegateCommand(async () => await LoadHistoryAsync());
            ShowLogDetailsCommand = new DelegateCommand<DownloadLog>(ExecuteShowLogDetails);
            LoadUsersCommand = new DelegateCommand(async () => await LoadUsersAsync());
            SetTimeCommand = new DelegateCommand(async () => await ExecuteSetTimeAsync());
            ResetToFactoryCommand = new DelegateCommand(async () => await ExecuteResetToFactoryAsync());
            ClearLogsCommand = new DelegateCommand(async () => await ExecuteClearLogsAsync());
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.OK));
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
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar el historial: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar el historial: {ex.Message}");
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
            var confirmed = await _messageService.ShowConfirmationAsync("Sincronizar Hora", "¿Desea sincronizar la fecha y hora del dispositivo con la hora actual de su zona horaria?");
            if (!confirmed) return;

            (IsWorking, WorkingMessage) = (true, "Ajustando la hora del reloj...");
            try
            {
                var result = await _mediator.Send(new SetDeviceTimeCommand(_device.DeviceId));
                IsWorking = false;
                if (result.IsSuccess) await _messageService.ShowSuccessAsync("Hora sincronizada correctamente.");
                else await _messageService.ShowErrorAsync($"Error: {result.Error}");
            }
            catch (Exception ex) { IsWorking = false; await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
        }

        private async Task ExecuteResetToFactoryAsync()
        {
            if (_device == null) return;
            var confirmed = await _messageService.ShowConfirmationAsync("Restablecer de fábrica",
                $"¡ADVERTENCIA! Se borrará toda la configuración de \"{_device.Name}\" y el reloj quedará como nuevo.\n\n" +
                "Descargue antes sus checadas: las que no estén en el sistema se perderán. ¿Desea continuar?");
            if (!confirmed) return;

            (IsWorking, WorkingMessage) = (true, "Enviando el restablecimiento al reloj...");
            try
            {
                var result = await _mediator.Send(new ResetDeviceToFactoryCommand(_device.DeviceId));
                IsWorking = false;
                if (result.IsSuccess) await _messageService.ShowSuccessAsync("Comando de restablecimiento enviado.");
                else await _messageService.ShowErrorAsync($"Error: {result.Error}");
            }
            catch (Exception ex) { IsWorking = false; await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
        }

        /// <summary>
        /// Vacía la memoria de checadas del reloj sin perder ninguna: primero descarga todo y sólo borra en el reloj el
        /// rango que quedó guardado, si la lectura fue completa (la misma limpieza segura que "Borrar tras descargar").
        /// En la base de datos nunca se borra nada.
        /// </summary>
        private async Task ExecuteClearLogsAsync()
        {
            if (_device == null) return;
            if (_device.DownloadMethod == DeviceDownloadMethod.Adms)
            {
                await _messageService.ShowWarningAsync("Los relojes ADMS envían sus checadas solos; el vaciado de memoria sólo está disponible para relojes con descarga SDK.");
                return;
            }

            var confirmed = await _messageService.ShowConfirmationAsync("Vaciar memoria del reloj",
                $"Se descargarán TODAS las checadas de \"{_device.Name}\" y, sólo si la descarga es completa, se borrarán del reloj las que quedaron guardadas en el sistema.\n\n" +
                "Las checadas ya guardadas en el sistema no se modifican. ¿Desea continuar?");
            if (!confirmed) return;

            IsClearingLogs = true;
            try
            {
                var result = await _mediator.Send(new DownloadFromDeviceCommand(_device.DeviceId,
                    InitiatedByUserId: _authService.CurrentUserId,
                    InitiatedByUserName: _authService.CurrentUserName,
                    ForceFullSync: true,
                    ClearDeviceAfterDownload: true));

                if (result.IsFailure)
                {
                    await _messageService.ShowErrorAsync($"No se vació la memoria: la descarga falló ({result.Error}).");
                }
                else if (result.Value.LogsCleared)
                {
                    await _messageService.ShowSuccessAsync($"Memoria del reloj vaciada. Checadas descargadas y guardadas: {result.Value.RecordsDownloaded}.");
                }
                else if (result.Value.RecordsDownloaded == 0 && result.Value.ClearErrorMessage == null)
                {
                    await _messageService.ShowSuccessAsync("El reloj no tenía checadas que vaciar.");
                }
                else
                {
                    await _messageService.ShowWarningAsync($"Se descargaron {result.Value.RecordsDownloaded} checadas, pero no se borraron del reloj: " +
                        (result.Value.ClearErrorMessage ?? "el reloj no confirmó el borrado."));
                }
                await LoadHistoryAsync();
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al vaciar la memoria: {ex.Message}");
            }
            finally
            {
                IsClearingLogs = false;
            }
        }

        private bool _isClearingLogs;
        public bool IsClearingLogs { get => _isClearingLogs; set => SetProperty(ref _isClearingLogs, value); }

        private void ExecuteShowLogDetails(DownloadLog log)
        {
            var parameters = new DialogParameters
            {
                { "Log", log },
                { "DeviceName", Name }
            };
            _dialogService.ShowDialog("DownloadLogDetailsDialog", parameters);
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

    /// <summary>Ocupación de una memoria del reloj (checadas, usuarios, huellas, rostros).</summary>
    public sealed class CapacityItem
    {
        public CapacityItem(string label, string icon, int? count, int? capacity)
        {
            Label = label;
            Icon = Enum.TryParse<MahApps.Metro.IconPacks.PackIconMaterialKind>(icon, out var kind) ? kind : MahApps.Metro.IconPacks.PackIconMaterialKind.Memory;
            Count = count;
            Capacity = capacity;
        }

        public string Label { get; }
        public MahApps.Metro.IconPacks.PackIconMaterialKind Icon { get; }
        public int? Count { get; }
        public int? Capacity { get; }
        public bool HasCapacity => Capacity is > 0;
        public double Percent => HasCapacity && Count.HasValue ? Math.Min(100, Count.Value * 100.0 / Capacity!.Value) : 0;
        /// <summary>A partir del 90 % conviene vaciar o depurar el reloj (como el aviso de la web).</summary>
        public bool IsAlmostFull => Percent >= 90;
        public string CountText => Count?.ToString("N0") ?? "—";
        public string CapacityText => HasCapacity ? $"de {Capacity:N0} ({Percent:0}%)" : "Capacidad desconocida";
    }
}
