using System.Windows;
using AttendanceSystem.WPF.Views;
using AttendanceSystem.WPF.Services;
using Prism.Ioc;
using Prism.Unity;
using Prism.Regions;
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
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.WPF
{
    public partial class App : PrismApplication
    {
        public IConfiguration Configuration { get; private set; } = null!;
        private ServiceProvider? _serviceProvider;

        protected override Window CreateShell()
        {
            var shell = Container.Resolve<ShellWindow>();
            
            // Navigate to Dashboard
            var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RegisterViewWithRegion("MainRegion", typeof(Views.Dashboard.DashboardView));
            
            return shell;
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);
            Configuration = builder.Build();
            containerRegistry.RegisterInstance(Configuration);

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .CreateLogger();

            // Build a ServiceProvider for MediatR and EF Core
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            var services = new ServiceCollection();
            
            // Add Configuration
            services.AddSingleton<IConfiguration>(Configuration);
            
            // Add Logging
            services.AddLogging(builder =>
            {
                builder.AddSerilog();
            });
            
            // Add MediatR
            services.AddMediatR(cfg => 
            {
                cfg.RegisterServicesFromAssembly(typeof(AttendanceSystem.Application.Features.Employees.Commands.CreateEmployeeCommand).Assembly);
            });
            
            // Add DbContext
            services.AddDbContext<AttendanceDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
            });
            // Register IDbContextFactory for services that require it (e.g. DeviceQueries)
            services.AddDbContextFactory<AttendanceDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
            });
            
            // Add Repositories
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            services.AddScoped<IDepartmentRepository, DepartmentRepository>();
            services.AddScoped<IPositionRepository, PositionRepository>();
            services.AddScoped<IBranchRepository, BranchRepository>();
            services.AddScoped<IShiftRepository, ShiftRepository>();
            services.AddScoped<IDeviceRepository, DeviceRepository>();
            services.AddScoped<IAttendanceRepository, AttendanceRepository>();
            services.AddScoped<IDailyAttendanceRepository, DailyAttendanceRepository>();
            services.AddScoped<IDownloadLogRepository, DownloadLogRepository>();
            services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
            
            // Add Queries (Dapper/EF Core Raw)
            services.AddScoped<IDepartmentQueries, DepartmentQueries>();
            services.AddScoped<IPositionQueries, PositionQueries>();
            services.AddScoped<IBranchQueries, BranchQueries>();
            services.AddScoped<IShiftQueries, ShiftQueries>();
            services.AddScoped<IDeviceQueries, DeviceQueries>();
            
            // Add UnitOfWork
            services.AddScoped<IUnitOfWork, AttendanceSystem.Infrastructure.Common.UnitOfWork>();
            
            // Add Services
            services.AddScoped<IImportService, ImportService>();
            services.AddScoped<IReportExportService, ReportExportService>();
            services.AddScoped<IBackupService, BackupService>();
            
            // Add JobScheduler implementation for WPF Client (No-Op)
            services.AddSingleton<IAttendanceJobScheduler, WpfAttendanceJobScheduler>();

            // Add Identity Core (UserManager only — sin cookies, SignInManager ni HTTP context)
            // Necesario para validar contraseñas contra la misma tabla AspNetUsers que usa Blazor
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 4;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AttendanceDbContext>();

            _serviceProvider = services.BuildServiceProvider();
            
            // Register services in Prism container
            // We register the IMediator instance resolved from our ServiceProvider
            containerRegistry.RegisterInstance<IMediator>(_serviceProvider.GetRequiredService<IMediator>());
            
            // Register factories for scoped services (DbContext and Repositories)
            // Using a factory pattern to get fresh scoped instances
            containerRegistry.RegisterSingleton<Func<AttendanceDbContext>>(() => 
                () => _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<AttendanceDbContext>());
            
            // WPF Services
            // AuthenticationStateService necesita UserManager<ApplicationUser> del ServiceProvider
            containerRegistry.RegisterSingleton<IAuthenticationStateService>(() =>
                new AuthenticationStateService(
                    _serviceProvider!.GetRequiredService<UserManager<ApplicationUser>>()));
            containerRegistry.RegisterSingleton<IFrameNavigationService, FrameNavigationService>();
            containerRegistry.RegisterSingleton<IMessageService, MessageService>();
            
            containerRegistry.RegisterInstance<IImportService>(_serviceProvider.GetRequiredService<IImportService>());
            containerRegistry.RegisterInstance<IReportExportService>(_serviceProvider.GetRequiredService<IReportExportService>());
            containerRegistry.RegisterInstance<IBackupService>(_serviceProvider.GetRequiredService<IBackupService>());

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
            containerRegistry.RegisterDialog<Views.Devices.DeviceAdvancedDetailsDialog, ViewModels.Devices.DeviceAdvancedDetailsViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
