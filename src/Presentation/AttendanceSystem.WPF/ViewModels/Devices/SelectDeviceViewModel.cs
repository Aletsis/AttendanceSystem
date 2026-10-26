using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Queries.GetActiveDevices;
using MediatR;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class SelectDeviceViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private readonly AttendanceSystem.WPF.Services.IMessageService _messageService;
        private ObservableCollection<DeviceDto> _devices = new();
        private DeviceDto? _selectedDevice;

        public string Title => "Seleccionar Dispositivo";

        public DialogCloseListener RequestClose { get; }

        public ObservableCollection<DeviceDto> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
        }

        public DeviceDto? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    ((DelegateCommand)SelectCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SelectCommand { get; }
        public ICommand CancelCommand { get; }

        public SelectDeviceViewModel(IMediator mediator, AttendanceSystem.WPF.Services.IMessageService messageService)
        {
            _mediator = mediator;
            _messageService = messageService;
            SelectCommand = new DelegateCommand(ExecuteSelect, CanExecuteSelect);
            CancelCommand = new DelegateCommand(ExecuteCancel);

            LoadDevices();
        }

        private async void LoadDevices()
        {
            try
            {
                var result = await _mediator.Send(new GetActiveDevicesQuery());
                if (result.IsSuccess)
                {
                    Devices = new ObservableCollection<DeviceDto>(result.Value);
                    if (Devices.Any())
                    {
                        SelectedDevice = Devices.First();
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
        }

        private bool CanExecuteSelect() => SelectedDevice != null;

        private void ExecuteSelect()
        {
            if (SelectedDevice == null) return;
            var parameters = new DialogParameters { { "DeviceId", SelectedDevice.DeviceId }, { "DeviceName", SelectedDevice.Name } };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        private void ExecuteCancel()
        {
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) { }
    }
}
