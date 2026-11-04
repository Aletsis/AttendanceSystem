using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.WPF.Views;
using AttendanceSystem.WPF.Services;
using Prism.Ioc;
using Prism.Unity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Serilog;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Services;
using MediatR;

namespace AttendanceSystem.WPF
{
    public partial class App : PrismApplication
    {
        public IConfiguration Configuration { get; private set; } = null!;
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Un error no controlado (p. ej. en el resultado de un diálogo) no debe cerrar la aplicación sin aviso.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error(args.Exception, "Tarea en segundo plano con error no observado");
                args.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Fatal(args.ExceptionObject as Exception, "Error no controlado; la aplicación se cerrará");

            // Fechas, horas y moneda en español de México en toda la interfaz (WPF usa en-US si no se indica).
            var culture = new System.Globalization.CultureInfo("es-MX");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            // Las rutas relativas (logs, respaldos sin carpeta configurada) son de la carpeta del programa: un acceso
            // directo puede arrancarlo en otra, o en una sin permiso de escritura.
            Environment.CurrentDirectory = AppContext.BaseDirectory;

            // Sin configuración no hay base a la que conectarse: antes se cerraba sin aviso al arrancar.
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
            {
                MessageBox.Show($"No se encontró la configuración del programa (appsettings.json) en:\n{AppContext.BaseDirectory}\n\n" +
                    "Vuelva a ejecutar el instalador o cree el archivo a partir de appsettings.example.json.",
                    "Sistema de Asistencia", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // La ventana de espera no debe cerrar la aplicación al cerrarse.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var wait = ShowWaitWindow();
            string? startupWarning;
            try
            {
                var settings = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .Build();
                var standalone = settings.GetValue<bool>(WpfBackendHost.StandaloneKey);
                _host = WpfBackendHost.Build(standalone);
                Configuration = _host.Services.GetRequiredService<IConfiguration>();
                if (string.IsNullOrWhiteSpace(Configuration.GetConnectionString("AttendanceDb")))
                {
                    throw new InvalidOperationException("La configuración no tiene la cadena de conexión ConnectionStrings:AttendanceDb.");
                }

                var host = _host;
                startupWarning = await Task.Run(() => StartBackendAsync(host, standalone));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "No se pudo iniciar la aplicación");
                wait.Close();
                MessageBox.Show($"No se pudo iniciar el programa: {ex.Message}", "Sistema de Asistencia", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            wait.Close();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.OnStartup(e);

            if (startupWarning != null)
            {
                MessageBox.Show(MainWindow!, startupWarning, "Sistema de Asistencia", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Deja la base lista y arranca lo que corre en segundo plano (tareas, consolidación, outbox, receptor ADMS).
        /// Devuelve un aviso para el usuario si algo quedó sin arrancar; la interfaz se abre de todos modos.
        /// </summary>
        private static async Task<string?> StartBackendAsync(IHost host, bool standalone)
        {
            var services = host.Services;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("WpfStartup");
            DatabaseStartupStatus status;
            try
            {
                status = await ServerStartup.PrepareDatabaseAsync(services, applyMigrations: standalone, logger);
            }
            catch (Exception ex)
            {
                // Sin base no se arranca nada en segundo plano: Hangfire no reintenta su arranque.
                logger.LogError(ex, "No se pudo preparar la base de datos al iniciar");
                return $"No se pudo conectar con la base de datos ({ex.Message}). Las tareas automáticas no se iniciaron; " +
                       "cierre y vuelva a abrir el programa cuando la base esté disponible.";
            }

            try
            {
                await ServerStartup.ScheduleRecurringJobsAsync(services, status == DatabaseStartupStatus.Ready, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudieron programar las tareas recurrentes al iniciar");
            }

            try
            {
                await host.StartAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudieron iniciar los servicios en segundo plano");
                return standalone
                    ? $"No se pudo iniciar el receptor de los relojes ADMS ni las tareas automáticas ({ex.Message}). Revise que el puerto ADMS no lo use otro programa."
                    : $"No se pudieron iniciar las tareas automáticas ({ex.Message}).";
            }

            return status switch
            {
                DatabaseStartupStatus.SchemaOutdated =>
                    "La base de datos todavía no está actualizada a esta versión del programa. Abra el sitio web para que la actualice " +
                    "y vuelva a abrir este programa; mientras tanto las tareas automáticas quedan en pausa.",
                DatabaseStartupStatus.Maintenance =>
                    "La base de datos está en mantenimiento: las tareas automáticas quedan en pausa hasta restaurar un respaldo.",
                _ => null
            };
        }

        private static Window ShowWaitWindow()
        {
            var window = new Window
            {
                Title = "Sistema de Asistencia",
                Content = new TextBlock { Text = "Preparando la base de datos…", Margin = new Thickness(32, 24, 32, 24), FontSize = 14 },
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = true
            };
            window.Show();
            return window;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var error = e.Exception is AggregateException { InnerException: { } inner } ? inner : e.Exception;
            Log.Error(error, "Error no controlado en la interfaz");
            var message = error is DatabaseMaintenanceException
                ? "El sistema está en mantenimiento de la base de datos y no se pueden guardar cambios en este momento. Intente más tarde."
                : $"Ocurrió un error inesperado: {error.Message}\n\nEl detalle quedó en el registro de la aplicación.";
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<ShellWindow>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // Se arranca en el inicio de sesión: sin usuario los comandos no se restringen. Al entrar se navega al inicio.
            Container.Resolve<IFrameNavigationService>().ResetTo(FrameNavigationService.LoginViewName);
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            var services = _host!.Services;
            containerRegistry.RegisterInstance(Configuration);

            // Register services in Prism container
            // Usar WpfScopedMediator para ejecutar cada operación en un IServiceScope aislado y dispuesto inmediatamente (H15)
            containerRegistry.RegisterInstance<IMediator>(new WpfScopedMediator(services));

            // WPF Services
            containerRegistry.RegisterInstance<IAuthenticationStateService>(services.GetRequiredService<IAuthenticationStateService>());
            containerRegistry.RegisterInstance<IRestoreStateService>(services.GetRequiredService<IRestoreStateService>());
            containerRegistry.RegisterSingleton<IFrameNavigationService, FrameNavigationService>();
            containerRegistry.RegisterSingleton<IMessageService, MessageService>();
            containerRegistry.RegisterSingleton<ViewModels.ShellViewModel>();

            containerRegistry.RegisterInstance<IImportService>(services.GetRequiredService<IImportService>());
            containerRegistry.RegisterInstance<IReportExportService>(services.GetRequiredService<IReportExportService>());
            // IBackupService no se registra aquí: es scoped (usa la base de datos) y las pantallas de respaldo usan los
            // comandos de MediatR, que crean su propio ámbito.

            // ViewModels
            containerRegistry.RegisterForNavigation<Views.Employees.EmployeesView, ViewModels.Employees.EmployeesViewModel>();
            containerRegistry.RegisterForNavigation<Views.Employees.EmployeeDetailView, ViewModels.Employees.EmployeeDetailViewModel>();
            containerRegistry.RegisterForNavigation<Views.Dashboard.DashboardView, ViewModels.Dashboard.DashboardViewModel>();
            containerRegistry.RegisterForNavigation<Views.Departments.DepartmentsView, ViewModels.Departments.DepartmentsViewModel>();
            containerRegistry.RegisterForNavigation<Views.Positions.PositionsView, ViewModels.Positions.PositionsViewModel>();
            containerRegistry.RegisterForNavigation<Views.Branches.BranchesView, ViewModels.Branches.BranchesViewModel>();
            containerRegistry.RegisterForNavigation<Views.Shifts.ShiftsView, ViewModels.Shifts.ShiftsViewModel>();
            containerRegistry.RegisterForNavigation<Views.Devices.DevicesView, ViewModels.Devices.DevicesViewModel>();
            containerRegistry.RegisterForNavigation<Views.Attendance.AttendanceView, ViewModels.Attendance.AttendanceViewModel>();
            containerRegistry.RegisterForNavigation<Views.Attendance.PunchLogView, ViewModels.Attendance.PunchLogViewModel>();
            containerRegistry.RegisterForNavigation<Views.Reports.ReportsView, ViewModels.Reports.ReportsViewModel>();
            containerRegistry.RegisterForNavigation<Views.Settings.SettingsView, ViewModels.Settings.SettingsViewModel>();
            containerRegistry.RegisterForNavigation<Views.Backup.BackupView, ViewModels.Backup.BackupViewModel>();
            containerRegistry.RegisterForNavigation<Views.Auth.LoginView, ViewModels.Auth.LoginViewModel>();

            // Dialogs
            containerRegistry.RegisterDialog<Views.Devices.SelectDeviceDialog, ViewModels.Devices.SelectDeviceViewModel>();
            containerRegistry.RegisterDialog<Views.Branches.BranchDetailDialog, ViewModels.Branches.BranchDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Departments.DepartmentDetailDialog, ViewModels.Departments.DepartmentDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Positions.PositionDetailDialog, ViewModels.Positions.PositionDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Shifts.ShiftDetailDialog, ViewModels.Shifts.ShiftDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Devices.DeviceDetailDialog, ViewModels.Devices.DeviceDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Devices.DownloadLogDetailsDialog, ViewModels.Devices.DownloadLogDetailsViewModel>();
            containerRegistry.RegisterDialog<Views.Devices.DownloadLogsRangeDialog, ViewModels.Devices.DownloadLogsRangeViewModel>();
            containerRegistry.RegisterDialog<Views.Common.EffectiveDateDialog, ViewModels.Common.EffectiveDateViewModel>();
            containerRegistry.RegisterDialog<Views.Common.ReasonDialog, ViewModels.Common.ReasonViewModel>();
            containerRegistry.RegisterDialog<Views.Devices.DeviceAdvancedDetailsDialog, ViewModels.Devices.DeviceAdvancedDetailsViewModel>();
            containerRegistry.RegisterDialog<Views.Employees.ExternalEmployeeDetailDialog, ViewModels.Employees.ExternalEmployeeDetailViewModel>();
            containerRegistry.RegisterDialog<Views.Auth.ChangePasswordDialog, ViewModels.Auth.ChangePasswordViewModel>();
            containerRegistry.RegisterDialog<Views.Attendance.ManualPunchDialog, ViewModels.Attendance.ManualPunchViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                // Las tareas en curso reciben la cancelación y se espera a que terminen (Hangfire, hasta 30 s); una que
                // no alcance a terminar la retoma otro proceso al vencer su reserva.
                try
                {
                    Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(30))).Wait(TimeSpan.FromSeconds(35));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error al detener los servicios en segundo plano");
                }
                (_host as IDisposable)?.Dispose();
            }
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
