using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;
using AttendanceSystem.Application.Features.Devices.Commands.CreateDevice;
using AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;
using AttendanceSystem.Application.Features.Devices.Commands.RefreshDeviceInfo;
using AttendanceSystem.Application.Features.Devices.Commands.ImportUsersFromDevice;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Prism.Dialogs;
using System.Windows.Media;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DevicesViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IAuthenticationStateService _authService;

        private ObservableCollection<DeviceListItem> _devices = new();
        private ObservableCollection<DeviceListItem> _filteredDevices = new();
        private DeviceListItem? _selectedDevice;
        private string _searchText = string.Empty;
        private List<DeviceDto> _allDevicesData = new();

        public ObservableCollection<DeviceListItem> Devices
        {
            get => _filteredDevices;
            set => SetProperty(ref _filteredDevices, value);
        }

        public DeviceListItem? SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterDevices();
                }
            }
        }

        public ICommand AddDeviceCommand { get; }
        public ICommand EditDeviceCommand { get; }
        public ICommand DeleteDeviceCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand DownloadLogsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SyncEmployeesCommand { get; }
        public ICommand ViewDetailsCommand { get; }

        public DevicesViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator,
            IDialogService dialogService,
            IAuthenticationStateService authService)
        {
            _authService = authService;
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _dialogService = dialogService;

            AddDeviceCommand = new DelegateCommand(ExecuteAddDevice);
            EditDeviceCommand = new DelegateCommand<DeviceListItem?>(x => ExecuteEditDevice(x ?? SelectedDevice));
            DeleteDeviceCommand = new DelegateCommand<DeviceListItem?>(async x => await ExecuteDeleteDeviceAsync(x ?? SelectedDevice));
            TestConnectionCommand = new DelegateCommand<DeviceListItem?>(async x => await ExecuteTestConnectionAsync(x ?? SelectedDevice));
            DownloadLogsCommand = new DelegateCommand<DeviceListItem?>(async x => await ExecuteDownloadLogsAsync(x ?? SelectedDevice));
            SyncEmployeesCommand = new DelegateCommand<DeviceListItem?>(async x => await ExecuteSyncEmployeesAsync(x ?? SelectedDevice));
            ViewDetailsCommand = new DelegateCommand<DeviceListItem?>(x => ExecuteViewDetails(x ?? SelectedDevice));
            RefreshCommand = new DelegateCommand(async () => await LoadDevicesAsync());

            _ = LoadDevicesAsync();
        }

        private async Task LoadDevicesAsync()
        {
            SetBusy(true, "Cargando dispositivos...");
            try
            {
                var result = await _mediator.Send(new GetAllDevicesQuery());

                if (result.IsSuccess && result.Value != null)
                {
                    _allDevicesData = result.Value.ToList();
                    _devices.Clear();

                    foreach (var device in _allDevicesData)
                    {
                        _devices.Add(new DeviceListItem
                        {
                            Id = device.DeviceId,
                            Name = device.Name,
                            IpAddress = device.IpAddress,
                            Port = device.Port,
                            BranchName = device.Location ?? "N/A",
                            IsActive = device.IsActive,
                            Status = device.Status,
                            LastSync = device.LastDownloadAt
                        });
                    }
                    FilterDevices();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar dispositivos: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar dispositivos: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterDevices()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Devices = new ObservableCollection<DeviceListItem>(_devices);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _devices.Where(d =>
                    d.Name.ToLower().Contains(searchLower) ||
                    d.IpAddress.ToLower().Contains(searchLower) ||
                    d.BranchName.ToLower().Contains(searchLower));
                Devices = new ObservableCollection<DeviceListItem>(filtered);
            }
        }

        private void ExecuteAddDevice()
        {
            _dialogService.ShowDialog("DeviceDetailDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var command = new CreateDeviceCommand(
                        result.Parameters.GetValue<string>("Name"),
                        result.Parameters.GetValue<string>("IpAddress"),
                        result.Parameters.GetValue<int>("Port"),
                        result.Parameters.GetValue<string?>("Location"),
                        result.Parameters.GetValue<DeviceBrand>("Brand"),
                        result.Parameters.GetValue<bool>("ShouldClearAfterDownload"),
                        result.Parameters.GetValue<DeviceDownloadMethod>("DownloadMethod"),
                        result.Parameters.GetValue<string?>("SerialNumber"),
                        result.Parameters.GetValue<string?>("Username"),
                        result.Parameters.GetValue<string?>("Password"),
                        result.Parameters.GetValue<string?>("TimeZoneId")
                    );

                    var createResult = await _mediator.Send(command);
                    if (createResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Dispositivo creado correctamente.");
                        await LoadDevicesAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al crear: {createResult.Error}");
                    }
                }
            });
        }

        private void ExecuteEditDevice(DeviceListItem? item)
        {
            if (item == null) return;
            var deviceData = _allDevicesData.FirstOrDefault(d => d.DeviceId == item.Id);
            if (deviceData == null) return;

            var parameters = new DialogParameters
            {
                { "DeviceId", deviceData.DeviceId },
                { "Name", deviceData.Name },
                { "IpAddress", deviceData.IpAddress },
                { "Port", deviceData.Port },
                { "Location", deviceData.Location ?? string.Empty },
                { "Brand", deviceData.Brand },
                { "DownloadMethod", deviceData.DownloadMethod },
                { "SerialNumber", deviceData.SerialNumber ?? string.Empty },
                { "ShouldClearAfterDownload", deviceData.ShouldClearAfterDownload },
                { "Username", deviceData.Username ?? string.Empty },
                { "Password", deviceData.Password ?? string.Empty },
                { "TimeZoneId", deviceData.TimeZoneId ?? string.Empty }
            };

            _dialogService.ShowDialog("DeviceDetailDialog", parameters, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var command = new UpdateDeviceCommand(
                        Guid.Parse(deviceData.DeviceId),
                        result.Parameters.GetValue<string>("Name"),
                        result.Parameters.GetValue<string>("IpAddress"),
                        result.Parameters.GetValue<int>("Port"),
                        result.Parameters.GetValue<string?>("Location"),
                        result.Parameters.GetValue<DeviceBrand>("Brand"),
                        result.Parameters.GetValue<bool>("ShouldClearAfterDownload"),
                        result.Parameters.GetValue<DeviceDownloadMethod>("DownloadMethod"),
                        result.Parameters.GetValue<string?>("SerialNumber"),
                        result.Parameters.GetValue<string?>("Username"),
                        result.Parameters.GetValue<string?>("Password"),
                        result.Parameters.GetValue<string?>("TimeZoneId")
                    );

                    var updateResult = await _mediator.Send(command);
                    if (updateResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Dispositivo actualizado correctamente.");
                        await LoadDevicesAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al actualizar: {updateResult.Error}");
                    }
                }
            });
        }

        private async Task ExecuteDeleteDeviceAsync(DeviceListItem? item)
        {
            if (item == null) return;
            var confirmed = await _messageService.ShowConfirmationAsync("Confirmar eliminación", $"¿Está seguro de eliminar el dispositivo {item.Name}?");
            if (!confirmed) return;

            await _messageService.ShowMessageAsync("No Implementado", "La funcionalidad de eliminar dispositivo aún no está implementada en el backend.");
        }

        private async Task ExecuteTestConnectionAsync(DeviceListItem? item)
        {
            if (item == null) return;
            SetBusy(true, $"Conectando con {item.Name}...");
            try
            {
                var result = await _mediator.Send(new RefreshDeviceInfoCommand(Guid.Parse(item.Id)));
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Conexión exitosa. Información del dispositivo actualizada.");
                    await LoadDevicesAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Fallo en la conexión: {result.Error}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteDownloadLogsAsync(DeviceListItem? item)
        {
            if (item == null) return;

            _dialogService.ShowDialog("DownloadLogsRangeDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var fromDate = result.Parameters.GetValue<DateTime?>("FromDate");
                    var toDate = result.Parameters.GetValue<DateTime?>("ToDate");

                    SetBusy(true, $"Descargando registros de {item.Name}...");
                    try
                    {
                        // La bitácora de descargas registra quién la inició, igual que en la versión web.
                        var command = new DownloadFromDeviceCommand(item.Id, fromDate, toDate, true,
                            _authService.CurrentUserId, _authService.CurrentUserName);
                        var downloadResult = await _mediator.Send(command);

                        if (downloadResult.IsSuccess)
                        {
                            await _messageService.ShowSuccessAsync($"Sincronización completada.\nTotal: {downloadResult.Value.RecordsDownloaded} registros.");
                            await LoadDevicesAsync();
                        }
                        else
                        {
                            await _messageService.ShowErrorAsync($"Error al descargar: {downloadResult.Error}");
                        }
                    }
                    catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
                    finally { SetBusy(false); }
                }
            });
        }

        private async Task ExecuteSyncEmployeesAsync(DeviceListItem? item)
        {
            if (item == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Sincronizar Empleados",
                $"¿Desea importar los usuarios y datos biométricos desde '{item.Name}'?\nEsto actualizará la base de datos local.");

            if (!confirmed) return;

            SetBusy(true, "Sincronizando empleados...");
            try
            {
                var result = await _mediator.Send(new ImportUsersFromDeviceCommand(item.Id));
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync($"Sincronización exitosa. {result.Value} empleados procesados.");
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al sincronizar: {result.Error}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private void ExecuteViewDetails(DeviceListItem? item)
        {
            if (item == null) return;

            var deviceData = _allDevicesData.FirstOrDefault(d => d.DeviceId == item.Id);
            if (deviceData == null) return;

            var parameters = new DialogParameters { { "Device", deviceData } };
            _dialogService.ShowDialog("DeviceAdvancedDetailsDialog", parameters);
        }
    }

    public class DeviceListItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string Status { get; set; } = "Desconectado";
        public DateTime? LastSync { get; set; }
    }
}
