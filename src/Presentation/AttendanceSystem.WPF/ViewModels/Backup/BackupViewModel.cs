using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;

namespace AttendanceSystem.WPF.ViewModels.Backup
{
    public class BackupViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;

        private ObservableCollection<BackupFileItem> _backupFiles = new();
        private BackupFileItem? _selectedBackup;

        public ObservableCollection<BackupFileItem> BackupFiles { get => _backupFiles; set => SetProperty(ref _backupFiles, value); }
        public BackupFileItem? SelectedBackup { get => _selectedBackup; set => SetProperty(ref _selectedBackup, value); }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DownloadBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public BackupViewModel(IFrameNavigationService navigationService, IMessageService messageService)
        {
            _navigationService = navigationService;
            _messageService = messageService;

            CreateBackupCommand = new DelegateCommand(async () => await ExecuteCreateBackupAsync());
            RestoreBackupCommand = new DelegateCommand(async () => await ExecuteRestoreBackupAsync(), CanExecuteBackupAction).ObservesProperty(() => SelectedBackup);
            DownloadBackupCommand = new DelegateCommand(async () => await ExecuteDownloadBackupAsync(), CanExecuteBackupAction).ObservesProperty(() => SelectedBackup);
            DeleteBackupCommand = new DelegateCommand(async () => await ExecuteDeleteBackupAsync(), CanExecuteBackupAction).ObservesProperty(() => SelectedBackup);
            RefreshCommand = new DelegateCommand(async () => await LoadBackupsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadBackupsAsync();
        }

        private async Task LoadBackupsAsync()
        {
            SetBusy(true, "Cargando respaldos...");
            try
            {
                await Task.Delay(100);
                // TODO: Load backups from file system
                BackupFiles.Clear();
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteCreateBackupAsync()
        {
            SetBusy(true, "Creando respaldo de la base de datos...");
            try
            {
                await Task.Delay(3000); // Simulate backup creation
                await _messageService.ShowSuccessAsync("Respaldo creado correctamente");
                await LoadBackupsAsync();
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error al crear respaldo: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteRestoreBackupAsync()
        {
            if (SelectedBackup == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar Restauración",
                $"¿Está seguro de restaurar el respaldo '{SelectedBackup.FileName}'?\n\nEsto sobrescribirá la base de datos actual.\nLa aplicación se reiniciará después de la restauración.");

            if (!confirmed) return;

            SetBusy(true, "Restaurando base de datos...");
            try
            {
                await Task.Delay(4000); // Simulate restore
                await _messageService.ShowSuccessAsync("Respaldo restaurado correctamente.\nPor favor, reinicie la aplicación.");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error al restaurar: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteDownloadBackupAsync()
        {
            if (SelectedBackup == null) return;

            SetBusy(true, "Descargando respaldo...");
            try
            {
                await Task.Delay(1000);
                await _messageService.ShowSuccessAsync($"Respaldo '{SelectedBackup.FileName}' descargado correctamente");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecuteDeleteBackupAsync()
        {
            if (SelectedBackup == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar Eliminación",
                $"¿Está seguro de eliminar el respaldo '{SelectedBackup.FileName}'?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando respaldo...");
            try
            {
                await Task.Delay(500);
                await _messageService.ShowSuccessAsync("Respaldo eliminado correctamente");
                await LoadBackupsAsync();
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private bool CanExecuteBackupAction() => SelectedBackup != null;
    }

    public class BackupFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted => FormatFileSize(FileSizeBytes);
        public DateTime CreatedDate { get; set; }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
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
