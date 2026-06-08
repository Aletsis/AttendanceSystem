using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceByDownloadLog;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using MediatR;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DownloadLogDetailsViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private DownloadLog? _log;
        private string _deviceName = string.Empty;
        private bool _isLoadingRecords;
        private bool _showRecords;
        private ObservableCollection<AttendanceLogViewDto> _importedRecords = new();

        public string Title => "Detalles de Sincronización";
        public string DeviceName { get => _deviceName; set => SetProperty(ref _deviceName, value); }
        public DateTime StartedAt => _log?.StartedAt.ToLocalTime() ?? DateTime.MinValue;
        public DateTime? CompletedAt => _log?.CompletedAt?.ToLocalTime();
        public string StatusText => _log == null ? "" : (!_log.CompletedAt.HasValue ? "En curso" : (_log.IsSuccessful ? "Exitoso" : "Fallido"));
        public Brush StatusColor => _log == null ? Brushes.Gray : (!_log.CompletedAt.HasValue ? Brushes.Orange : (_log.IsSuccessful ? Brushes.Green : Brushes.Red));
        public string? InitiatedBy => _log?.InitiatedByUserName;
        public int TotalRecords => _log?.TotalRecordsDownloaded ?? 0;
        public int NewRecords => _log?.NewRecordsAdded ?? 0;
        public bool HasError => !string.IsNullOrEmpty(_log?.ErrorMessage);
        public string? ErrorMessage => _log?.ErrorMessage;

        public bool IsLoadingRecords { get => _isLoadingRecords; set => SetProperty(ref _isLoadingRecords, value); }
        public bool ShowRecords 
        { 
            get => _showRecords; 
            set 
            {
                if (SetProperty(ref _showRecords, value) && value && _importedRecords.Count == 0)
                {
                    _ = LoadRecordsAsync();
                }
            } 
        }
        public ObservableCollection<AttendanceLogViewDto> ImportedRecords { get => _importedRecords; set => SetProperty(ref _importedRecords, value); }

        public ICommand CancelCommand { get; }

        public event Action<IDialogResult>? RequestClose;

        public DownloadLogDetailsViewModel(IMediator mediator)
        {
            _mediator = mediator;
            CancelCommand = new DelegateCommand(() => RequestClose?.Invoke(new DialogResult(ButtonResult.OK)));
        }

        private async Task LoadRecordsAsync()
        {
            if (_log == null) return;
            IsLoadingRecords = true;
            try
            {
                var result = await _mediator.Send(new GetAttendanceByDownloadLogQuery(_log.Id));
                ImportedRecords = new ObservableCollection<AttendanceLogViewDto>(result);
            }
            finally
            {
                IsLoadingRecords = false;
            }
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Log"))
            {
                _log = parameters.GetValue<DownloadLog>("Log");
                DeviceName = parameters.GetValue<string>("DeviceName");
                RaisePropertyChanged(string.Empty); // Update all properties
            }
        }
    }
}
