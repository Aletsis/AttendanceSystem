using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Domain.Enumerations;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DeviceDetailViewModel : BindableBase, IDialogAware
    {
        private string? _deviceId;
        private string _name = string.Empty;
        private string _ipAddress = string.Empty;
        private int _port = 4370;
        private string? _location;
        private DeviceBrand _selectedBrand = DeviceBrand.ZKTeco;
        private DeviceDownloadMethod _selectedDownloadMethod = DeviceDownloadMethod.Sdk;
        private string? _serialNumber;
        private bool _shouldClearAfterDownload;
        private string? _username;
        private string? _password;
        private string _title = "Nuevo Dispositivo";

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }
        public int Port { get => _port; set => SetProperty(ref _port, value); }
        public string? Location { get => _location; set => SetProperty(ref _location, value); }
        public DeviceBrand SelectedBrand { get => _selectedBrand; set => SetProperty(ref _selectedBrand, value); }
        public DeviceDownloadMethod SelectedDownloadMethod { get => _selectedDownloadMethod; set => SetProperty(ref _selectedDownloadMethod, value); }
        public string? SerialNumber { get => _serialNumber; set => SetProperty(ref _serialNumber, value); }
        public bool ShouldClearAfterDownload { get => _shouldClearAfterDownload; set => SetProperty(ref _shouldClearAfterDownload, value); }
        public string? Username { get => _username; set => SetProperty(ref _username, value); }
        public string? Password { get => _password; set => SetProperty(ref _password, value); }

        public IEnumerable<DeviceBrand> Brands => Enum.GetValues(typeof(DeviceBrand)).Cast<DeviceBrand>();
        public IEnumerable<DeviceDownloadMethod> DownloadMethods => Enum.GetValues(typeof(DeviceDownloadMethod)).Cast<DeviceDownloadMethod>();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; }

        public DeviceDetailViewModel()
        {
            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave)
                .ObservesProperty(() => Name)
                .ObservesProperty(() => IpAddress);
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private bool CanExecuteSave()
        {
            return !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(IpAddress);
        }

        private void ExecuteSave()
        {
            var parameters = new DialogParameters
            {
                { "DeviceId", _deviceId },
                { "Name", Name },
                { "IpAddress", IpAddress },
                { "Port", Port },
                { "Location", Location },
                { "Brand", SelectedBrand },
                { "DownloadMethod", SelectedDownloadMethod },
                { "SerialNumber", SerialNumber },
                { "ShouldClearAfterDownload", ShouldClearAfterDownload },
                { "Username", Username },
                { "Password", Password }
            };

            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        private void ExecuteCancel()
        {
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("DeviceId"))
            {
                _deviceId = parameters.GetValue<string>("DeviceId");
                Name = parameters.GetValue<string>("Name");
                IpAddress = parameters.GetValue<string>("IpAddress");
                Port = parameters.GetValue<int>("Port");
                Location = parameters.GetValue<string?>("Location");
                SelectedBrand = parameters.GetValue<DeviceBrand>("Brand");
                SelectedDownloadMethod = parameters.GetValue<DeviceDownloadMethod>("DownloadMethod");
                SerialNumber = parameters.GetValue<string?>("SerialNumber");
                ShouldClearAfterDownload = parameters.GetValue<bool>("ShouldClearAfterDownload");
                Username = parameters.GetValue<string?>("Username");
                Password = parameters.GetValue<string?>("Password");
                Title = "Editar Dispositivo";
            }
        }
    }
}
