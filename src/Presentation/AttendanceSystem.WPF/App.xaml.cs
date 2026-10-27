using System.Windows;
using System.Windows.Threading;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.WPF.Views;
using AttendanceSystem.WPF.Services;
using Prism.Ioc;
using Prism.Unity;
using Prism.Navigation.Regions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Serilog;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Services;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Domain.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Departments.Queries;
using AttendanceSystem.Application.Features.Positions.Queries;
using AttendanceSystem.Application.Features.Branches.Queries;
using AttendanceSystem.Application.Features.Shifts.Queries;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Infrastructure.Persistence.Queries;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Application.DependencyInjection;
using AttendanceSystem.Infrastructure.DependencyInjection;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.WPF
{
    public partial class App : PrismApplication
    {
        public IConfiguration Configuration { get; private set; } = null!;
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
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

            base.OnStartup(e);
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
            // Configuration
            var builder = new ConfigurationBuilder()
                // La carpeta del ejecutable, no la de trabajo: un acceso directo puede arrancar en otra carpeta.
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);
            Configuration = builder.Build();
            containerRegistry.RegisterInstance(Configuration);

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .CreateLogger();

            // Build a ServiceProvider for MediatR and EF Core
            var services = new ServiceCollection();

            // Add Configuration & Logging
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddLogging(builder =>
            {
                builder.AddSerilog();
            });

            // Registrar servicios compartidos de Aplicación e Infraestructura (H13 & H15)
            services.AddApplicationServices();
            services.AddInfrastructureServices(Configuration);

            // Servicios específicos de cliente WPF
            services.AddSingleton<IAuthenticationStateService, AuthenticationStateService>();
            services.AddSingleton<ICurrentUserService, WpfCurrentUserService>();
            services.AddSingleton<IBackgroundJobControlService, NullBackgroundJobControlService>();
            services.AddSingleton<IAttendanceJobScheduler, WpfAttendanceJobScheduler>();

            // Add Identity Core (UserManager only — sin cookies, SignInManager ni HTTP context)
            // Necesario para validar contraseñas contra la misma tabla AspNetUsers que usa Blazor
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Misma política que la versión web: al cambiar la contraseña desde aquí no debe aceptarse una más débil.
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;

                // Configuración de bloqueo compartida con el servidor web (H14)
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AttendanceDbContext>();

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            // Register services in Prism container
            // Usar WpfScopedMediator para ejecutar cada operación en un IServiceScope aislado y dispuesto inmediatamente (H15)
            containerRegistry.RegisterInstance<IMediator>(new WpfScopedMediator(_serviceProvider));

            // WPF Services
            containerRegistry.RegisterInstance<IAuthenticationStateService>(_serviceProvider.GetRequiredService<IAuthenticationStateService>());
            containerRegistry.RegisterInstance<ICurrentUserService>(_serviceProvider.GetRequiredService<ICurrentUserService>());
            containerRegistry.RegisterInstance<IRestoreStateService>(_serviceProvider.GetRequiredService<IRestoreStateService>());
            containerRegistry.RegisterSingleton<IFrameNavigationService, FrameNavigationService>();
            containerRegistry.RegisterSingleton<IMessageService, MessageService>();
            containerRegistry.RegisterSingleton<ViewModels.ShellViewModel>();

            containerRegistry.RegisterInstance<IImportService>(_serviceProvider.GetRequiredService<IImportService>());
            containerRegistry.RegisterInstance<IReportExportService>(_serviceProvider.GetRequiredService<IReportExportService>());
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
            _serviceProvider?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
