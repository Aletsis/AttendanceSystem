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

    // Comida (F06-05): ventana global en la que una ausencia marcada cuenta como comida y minutos con goce.
    public TimeSpan LunchWindowStart { get; private set; } = Services.LunchPolicy.DefaultWindowStart;
    public TimeSpan LunchWindowEnd { get; private set; } = Services.LunchPolicy.DefaultWindowEnd;
    public int PaidLunchMinutes { get; private set; } = Services.LunchPolicy.DefaultPaidMinutes;
    // Caso 11: activa, la primera ausencia que se traslapa con la ventana es comida (se descuenta sólo el excedente
    // sobre los minutos con goce); apagada, toda ausencia es salida temporal que RH clasifica.
    public bool LunchWindowEnabled { get; private set; } = true;
    // Ventana de comida de turnos nocturnos; puede cruzar medianoche.
    public TimeSpan NightLunchWindowStart { get; private set; } = Services.LunchPolicy.DefaultNightWindowStart;
    public TimeSpan NightLunchWindowEnd { get; private set; } = Services.LunchPolicy.DefaultNightWindowEnd;

    /// <summary>Caso 14: minutos dentro de los cuales dos checadas son un toque doble.</summary>
    public int DoubleTapMinutes { get; private set; } = Services.LunchPolicy.DoubleTapMaxMinutes;
    public const int MaxDoubleTapMinutes = 60;

    public Services.LunchPolicy LunchPolicy => new(LunchWindowStart, LunchWindowEnd, PaidLunchMinutes, LunchWindowEnabled, DoubleTapMinutes);
    public Services.LunchPolicy NightLunchPolicy => new(NightLunchWindowStart, NightLunchWindowEnd, PaidLunchMinutes, LunchWindowEnabled, DoubleTapMinutes);

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

    // Cloud Sync Settings
    public bool IsCloudSyncEnabled { get; private set; }
    public string? CloudDbConnectionString { get; private set; }

    // Authoritative TimeZone
    public string DefaultTimeZoneId { get; private set; } = "America/Mexico_City";

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
            SmtpEnableSsl = true,
            IsCloudSyncEnabled = false,
            CloudDbConnectionString = null,
            DefaultTimeZoneId = "America/Mexico_City"
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

    public void UpdateBackupDirectory(string backupDirectory)
    {
        BackupDirectory = backupDirectory;
    }

    public void UpdateLunchSettings(TimeSpan windowStart, TimeSpan windowEnd, int paidLunchMinutes)
    {
        if (windowStart < TimeSpan.Zero || windowEnd >= TimeSpan.FromDays(1) || windowStart >= windowEnd)
            throw new DomainException("La ventana de comida debe tener un inicio anterior al fin dentro del mismo día");
        if (paidLunchMinutes < 0 || paidLunchMinutes > 240)
            throw new DomainException("Los minutos de comida con goce deben estar entre 0 y 240");

        LunchWindowStart = windowStart;
        LunchWindowEnd = windowEnd;
        PaidLunchMinutes = paidLunchMinutes;
    }

    // Caso 18: umbrales de la cuarentena de marcajes.
    public int QuarantineMinDateDaysBeforeDevice { get; private set; } = 30;
    public int QuarantineFutureToleranceMinutes { get; private set; } = 5;
    public int QuarantineBackwardJumpMinutes { get; private set; } = 60;
    public int QuarantineRealtimeLagMinutes { get; private set; } = 30;
    public int QuarantineClockDriftAlertMinutes { get; private set; } = 10;
    public int QuarantineSdkReadMarginDays { get; private set; } = 1;
    public int QuarantineClockSyncToleranceMinutes { get; private set; } = 2;

    public Services.PunchQuarantinePolicy QuarantinePolicy => new(QuarantineMinDateDaysBeforeDevice, QuarantineFutureToleranceMinutes,
        QuarantineBackwardJumpMinutes, QuarantineRealtimeLagMinutes, QuarantineClockDriftAlertMinutes, QuarantineSdkReadMarginDays,
        QuarantineClockSyncToleranceMinutes);

    public void UpdateQuarantinePolicy(Services.PunchQuarantinePolicy policy)
    {
        if (policy.Validate() is { } error) throw new DomainException(error);
        QuarantineMinDateDaysBeforeDevice = policy.MinDateDaysBeforeDevice;
        QuarantineFutureToleranceMinutes = policy.FutureToleranceMinutes;
        QuarantineBackwardJumpMinutes = policy.BackwardJumpMinutes;
        QuarantineRealtimeLagMinutes = policy.RealtimeLagMinutes;
        QuarantineClockDriftAlertMinutes = policy.ClockDriftAlertMinutes;
        QuarantineSdkReadMarginDays = policy.SdkReadMarginDays;
        QuarantineClockSyncToleranceMinutes = policy.ClockSyncToleranceMinutes;
    }

    public void UpdateDoubleTapMinutes(int minutes)
    {
        if (minutes < 1 || minutes > MaxDoubleTapMinutes)
            throw new DomainException($"El toque doble debe estar entre 1 y {MaxDoubleTapMinutes} minutos");
        DoubleTapMinutes = minutes;
    }

    public void SetLunchWindowEnabled(bool enabled)
    {
        LunchWindowEnabled = enabled;
    }

    public void UpdateNightLunchWindow(TimeSpan windowStart, TimeSpan windowEnd)
    {
        if (windowStart < TimeSpan.Zero || windowStart >= TimeSpan.FromDays(1) ||
            windowEnd < TimeSpan.Zero || windowEnd >= TimeSpan.FromDays(1) || windowStart == windowEnd)
            throw new DomainException("La ventana de comida nocturna debe tener inicio y fin distintos dentro de las 24 horas");

        NightLunchWindowStart = windowStart;
        NightLunchWindowEnd = windowEnd;
    }

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

    public void UpdateCloudSyncSettings(bool isCloudSyncEnabled, string? cloudDbConnectionString)
    {
        IsCloudSyncEnabled = isCloudSyncEnabled;
        CloudDbConnectionString = cloudDbConnectionString;
    }

    public void UpdateDefaultTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("La zona horaria predeterminada no puede estar vacía.");
        DefaultTimeZoneId = timeZoneId;
    }
}
