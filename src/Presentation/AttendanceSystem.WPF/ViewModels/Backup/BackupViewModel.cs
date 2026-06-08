using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Backup.Commands;
using AttendanceSystem.Application.Features.Backup.Queries;
using AttendanceSystem.Application.DTOs;
using Microsoft.Win32;
using System.IO;

namespace AttendanceSystem.WPF.ViewModels.Backup
{
    public class BackupViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<BackupDto> _backupFiles = new();
        private BackupDto? _selectedBackup;
        private string _backupDescription = string.Empty;

        public ObservableCollection<BackupDto> BackupFiles { get => _backupFiles; set => SetProperty(ref _backupFiles, value); }
        public BackupDto? SelectedBackup { get => _selectedBackup; set => SetProperty(ref _selectedBackup, value); }
        public string BackupDescription { get => _backupDescription; set => SetProperty(ref _backupDescription, value); }

        // Stats
        public int TotalBackups => BackupFiles.Count;
        public string UsedSpaceFormatted => FormatBytes(BackupFiles.Sum(b => b.SizeInBytes));
        public string LastBackupDate => BackupFiles.OrderByDescending(b => b.CreatedAt).FirstOrDefault()?.CreatedAt.ToString("dd/MM/yyyy HH:mm") ?? "N/A";

        public ICommand CreateFullBackupCommand { get; }
        public ICommand CreateDatabaseBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DownloadBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public BackupViewModel(
            IFrameNavigationService navigationService, 
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            CreateFullBackupCommand = new DelegateCommand(async () => await ExecuteCreateBackupAsync("Full"));
            CreateDatabaseBackupCommand = new DelegateCommand(async () => await ExecuteCreateBackupAsync("DatabaseOnly"));
            RestoreBackupCommand = new DelegateCommand<BackupDto>(async (b) => await ExecuteRestoreBackupAsync(b));
            DownloadBackupCommand = new DelegateCommand<BackupDto>(async (b) => await ExecuteDownloadBackupAsync(b));
            DeleteBackupCommand = new DelegateCommand<BackupDto>(async (b) => await ExecuteDeleteBackupAsync(b));
            RefreshCommand = new DelegateCommand(async () => await LoadBackupsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadBackupsAsync();
        }

        private async Task LoadBackupsAsync()
        {
            SetBusy(true, "Cargando respaldos...");
            try
            {
                var result = await _mediator.Send(new GetBackupsQuery());
                BackupFiles = new ObservableCollection<BackupDto>(result.OrderByDescending(b => b.CreatedAt));
                RaisePropertyChanged(nameof(TotalBackups));
                RaisePropertyChanged(nameof(UsedSpaceFormatted));
                RaisePropertyChanged(nameof(LastBackupDate));
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteCreateBackupAsync(string type)
        {
            SetBusy(true, $"Creando respaldo {(type == "Full" ? "completo" : "de base de datos")}...");
            try
            {
                var result = await _mediator.Send(new CreateBackupCommand(type, BackupDescription));
                if (result.Success)
                {
                    await _messageService.ShowSuccessAsync($"Respaldo creado correctamente: {Path.GetFileName(result.BackupFilePath ?? "")}");
                    BackupDescription = string.Empty;
                    await LoadBackupsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error: {result.Message}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteRestoreBackupAsync(BackupDto backup)
        {
            if (backup == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar Restauración",
                $"¿Está seguro de restaurar el respaldo '{backup.FileName}'?\n\nEsta acción sobrescribirá todos los datos actuales.\nLa aplicación se reiniciará automáticamente.");

            if (!confirmed) return;

            SetBusy(true, "Restaurando base de datos... Por favor espere.");
            try
            {
                var result = await _mediator.Send(new RestoreBackupCommand(backup.FilePath));
                if (result.Success)
                {
                    await _messageService.ShowSuccessAsync("✓ Restauración completada exitosamente. La aplicación debe reiniciarse.");
                    // En WPF podrías forzar un reinicio o cerrar la app.
                }
                else
                {
                    await _messageService.ShowErrorAsync($"✗ Error al restaurar: {result.Message}");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteDownloadBackupAsync(BackupDto backup)
        {
            if (backup == null) return;

            var saveDialog = new SaveFileDialog
            {
                FileName = backup.FileName,
                Filter = "Archivos de Respaldo (*.bak;*.zip)|*.bak;*.zip",
                Title = "Guardar Respaldo Como"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(backup.FilePath, saveDialog.FileName, true);
                    await _messageService.ShowSuccessAsync("Archivo guardado correctamente.");
                }
                catch (Exception ex) { await _messageService.ShowErrorAsync($"Error al guardar archivo: {ex.Message}"); }
            }
        }

        private async Task ExecuteDeleteBackupAsync(BackupDto backup)
        {
            if (backup == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar Eliminación",
                $"¿Está seguro de eliminar permanentemente el respaldo '{backup.FileName}'?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando respaldo...");
            try
            {
                var result = await _mediator.Send(new DeleteBackupCommand(backup.FilePath));
                if (result)
                {
                    await _messageService.ShowSuccessAsync("Respaldo eliminado.");
                    await LoadBackupsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync("No se pudo eliminar el archivo.");
                }
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
