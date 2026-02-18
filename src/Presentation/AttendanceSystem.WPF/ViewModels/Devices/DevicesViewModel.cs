using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DevicesViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<DeviceListItem> _devices = new();
        private DeviceListItem? _selectedDevice;
        private List<DeviceDto> _allDevicesData = new();

        public ObservableCollection<DeviceListItem> Devices { get => _devices; set => SetProperty(ref _devices, value); }
        public DeviceListItem? SelectedDevice { get => _selectedDevice; set => SetProperty(ref _selectedDevice, value); }

        public ICommand AddDeviceCommand { get; }
        public ICommand EditDeviceCommand { get; }
        public ICommand DeleteDeviceCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand DownloadLogsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public DevicesViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            AddDeviceCommand = new DelegateCommand(ExecuteAddDevice);
            EditDeviceCommand = new DelegateCommand(ExecuteEditDevice, CanExecuteEdit).ObservesProperty(() => SelectedDevice);
            DeleteDeviceCommand = new DelegateCommand(async () => await ExecuteDeleteDeviceAsync(), CanExecuteEdit).ObservesProperty(() => SelectedDevice);
            TestConnectionCommand = new DelegateCommand(async () => await ExecuteTestConnectionAsync(), CanExecuteEdit).ObservesProperty(() => SelectedDevice);
            DownloadLogsCommand = new DelegateCommand(async () => await ExecuteDownloadLogsAsync(), CanExecuteEdit).ObservesProperty(() => SelectedDevice);
            RefreshCommand = new DelegateCommand(async () => await LoadDevicesAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

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

        private void ExecuteAddDevice()
        {
            _messageService.ShowMessageAsync("Agregar Dispositivo", "Formulario de creación en desarrollo...");
        }

        private void ExecuteEditDevice()
        {
            if (SelectedDevice == null) return;
            _messageService.ShowMessageAsync("Editar Dispositivo", $"Formulario de edición para {SelectedDevice.Name} en desarrollo...");
        }

        private async Task ExecuteDeleteDeviceAsync()
        {
            if (SelectedDevice == null) return;
            var confirmed = await _messageService.ShowConfirmationAsync("Confirmar eliminación", $"¿Eliminar dispositivo {SelectedDevice.Name}?");
            if (!confirmed) return;

            // TODO: Implement DeleteDeviceCommand
            await _messageService.ShowMessageAsync("No Implementado", "La funcionalidad de eliminar dispositivo aún no está implementada en el backend.");
        }

        private async Task ExecuteTestConnectionAsync()
        {
            if (SelectedDevice == null) return;
            SetBusy(true, "Probando conexión...");
            try
            {
                await Task.Delay(1000); // Simulate connection test
                // TODO: Implement test connection via infrastructure service
                await _messageService.ShowMessageAsync("Simulación", "Conexión simulada exitosa al dispositivo");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteDownloadLogsAsync()
        {
            if (SelectedDevice == null) return;
            SetBusy(true, "Descargando registros...");
            try
            {
                await Task.Delay(2000); // Simulate download
                 // TODO: Implement download logs via infrastructure service
                await _messageService.ShowMessageAsync("Simulación", "Registros descargados correctamente (Simulado)");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private bool CanExecuteEdit()
        {
            return SelectedDevice != null;
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
