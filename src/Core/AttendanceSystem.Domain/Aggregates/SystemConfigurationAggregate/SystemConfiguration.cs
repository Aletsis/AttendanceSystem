using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;

public sealed class SystemConfiguration : AggregateRoot<Guid>
{
    // Singleton ID
    public static readonly Guid ConfigurationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public string CompanyName { get; private set; } = null!;
    public byte[]? CompanyLogo { get; private set; }

    public TimeSpan LateTolerance { get; private set; }
    public TimeSpan StandardWorkHours { get; private set; }
    public bool AutoClearDevicesAfterDownload { get; private set; }
    
    // Auto Download Settings
    public bool IsAutoDownloadEnabled { get; private set; }
    public TimeSpan? AutoDownloadTime { get; private set; } // Time from midnight

    // ADMS Settings
    public int AdmsPort { get; private set; }

    // Backup Settings
    public string BackupDirectory { get; private set; } = null!;
    public int BackupTimeoutMinutes { get; private set; } // Timeout para pg_dump
    public bool IsAutoBackupEnabled { get; private set; }
    public TimeSpan? AutoBackupTime { get; private set; }

    // Report Settings
    public bool IsAutoReportEnabled { get; private set; }
    public TimeSpan? AutoReportTime { get; private set; }
    public string? AutoReportEmails { get; private set; }
    public bool AutoReportForToday { get; private set; }

    // Work Period Settings
    public WorkPeriodMode WorkPeriodMode { get; private set; }
    public DayOfWeek WeeklyStartDay { get; private set; }
    public int FortnightFirstDay { get; private set; }
    public int FortnightSecondDay { get; private set; }
    public int MonthlyStartDay { get; private set; }

    // Alert Settings
    public bool AreAlertsEnabled { get; private set; }
    public string? AbsenceAlertEmails { get; private set; }
    public string? LateAlertEmails { get; private set; }
    public string? SystemFailureAlertEmails { get; private set; }
    
    // SMTP Settings
    public string? SmtpHost { get; private set; }
    public int SmtpPort { get; private set; }
    public string? SmtpUser { get; private set; }
    public string? SmtpPassword { get; private set; }
    public bool SmtpEnableSsl { get; private set; }

    private SystemConfiguration() { } // For EF

    public static SystemConfiguration CreateDefault()
    {
        return new SystemConfiguration
        {
            Id = ConfigurationId,
            CompanyName = "Mi Empresa",
            CompanyLogo = null,
            LateTolerance = TimeSpan.FromMinutes(15),
            StandardWorkHours = TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload = false,
            IsAutoDownloadEnabled = false,
            AutoDownloadTime = null,
            AutoDownloadOnlyToday = false,
            AdmsPort = 18373, // Puerto dedicado para ADMS
            BackupDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups"),
            BackupTimeoutMinutes = 10, // Timeout por defecto: 10 minutos
            IsAutoBackupEnabled = false,
            AutoBackupTime = null,
            IsAutoReportEnabled = false,
            AutoReportTime = null,
            AutoReportEmails = null,
            AutoReportForToday = false,
            WorkPeriodMode = WorkPeriodMode.Weekly,
            WeeklyStartDay = DayOfWeek.Monday,
            FortnightFirstDay = 1,
            FortnightSecondDay = 16,
            MonthlyStartDay = 1,
            AreAlertsEnabled = false,
            AbsenceAlertEmails = null,
            LateAlertEmails = null,
            SystemFailureAlertEmails = null,
            SmtpPort = 587,
            SmtpEnableSsl = true
        };
    }

    public void UpdateSettings(
        string companyName,
        byte[]? companyLogo,
        TimeSpan lateTolerance,
        TimeSpan standardWorkHours,
        bool autoClearDevicesAfterDownload,
        bool isAutoDownloadEnabled,
        TimeSpan? autoDownloadTime,
        bool autoDownloadOnlyToday,
        int admsPort,
        string backupDirectory,
        int backupTimeoutMinutes,
        bool areAlertsEnabled,
        string? absenceAlertEmails,
        string? lateAlertEmails,
        string? systemFailureAlertEmails,
        string? smtpHost,
        int smtpPort,
        string? smtpUser,
        string? smtpPassword,
        bool smtpEnableSsl,
        bool isAutoBackupEnabled,
        TimeSpan? autoBackupTime,
        bool isAutoReportEnabled,
        TimeSpan? autoReportTime,
        string? autoReportEmails,
        bool autoReportForToday)
    {
        CompanyName = companyName;
        CompanyLogo = companyLogo;
        LateTolerance = lateTolerance;
        StandardWorkHours = standardWorkHours;
        AutoClearDevicesAfterDownload = autoClearDevicesAfterDownload;
        IsAutoDownloadEnabled = isAutoDownloadEnabled;
        AutoDownloadTime = autoDownloadTime;
        AutoDownloadOnlyToday = autoDownloadOnlyToday;
        AdmsPort = admsPort;
        BackupDirectory = backupDirectory;
        BackupTimeoutMinutes = backupTimeoutMinutes;
        AreAlertsEnabled = areAlertsEnabled;
        AbsenceAlertEmails = absenceAlertEmails;
        LateAlertEmails = lateAlertEmails;
        SystemFailureAlertEmails = systemFailureAlertEmails;
        SmtpHost = smtpHost;
        SmtpPort = smtpPort;
        SmtpUser = smtpUser;
        SmtpPassword = smtpPassword;
        SmtpEnableSsl = smtpEnableSsl;
        IsAutoBackupEnabled = isAutoBackupEnabled;
        AutoBackupTime = autoBackupTime;
        IsAutoReportEnabled = isAutoReportEnabled;
        AutoReportTime = autoReportTime;
        AutoReportEmails = autoReportEmails;
        AutoReportForToday = autoReportForToday;
    }

    public bool AutoDownloadOnlyToday { get; private set; }

    public void UpdateWorkPeriodSettings(
        WorkPeriodMode mode,
        DayOfWeek weeklyStartDay,
        int fortnightFirstDay,
        int fortnightSecondDay,
        int monthlyStartDay)
    {
        WorkPeriodMode = mode;
        WeeklyStartDay = weeklyStartDay;
        FortnightFirstDay = fortnightFirstDay;
        FortnightSecondDay = fortnightSecondDay;
        MonthlyStartDay = monthlyStartDay;
    }
}
