using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.DTOs;

// DTOs de Asistencia
public sealed record AttendanceRecordDto(
    Guid Id,
    string EmployeeId,
    string DeviceId,
    DateTime CheckTime,
    string VerifyMethod,
    string CheckType,
    string Status);

public sealed record CreateAttendanceDto(
    string EmployeeId,
    string DeviceId,
    DateTime CheckTime,
    int VerifyMethodCode,
    int CheckTypeCode);

// DTOs de Dispositivos
public sealed record DeviceDto(
    string DeviceId,
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    bool IsActive,
    string Status,
    DeviceBrand Brand,
    DeviceDownloadMethod DownloadMethod,
    DateTime? LastDownloadAt,
    int TotalDownloadCount,
    bool ShouldClearAfterDownload = false,
    string? SerialNumber = null,
    string? FirmwareVersion = null,
    string? Platform = null,
    int? UserCount = null,
    int? FingerprintCount = null,
    int? FaceCount = null,
    int? AttendanceRecordCount = null,
    int? UserCapacity = null,
    int? FingerprintCapacity = null,
    int? FaceCapacity = null,
    int? AttendanceRecordCapacity = null,
    string? Username = null,
    string? Password = null);

public sealed record CreateDeviceDto(
    string DeviceId,
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    DeviceBrand Brand,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? Username = null,
    string? Password = null);

public sealed record DeviceInfoDto(
    string SerialNumber,
    string DeviceName,
    string FirmwareVersion,
    string Platform,
    int UserCount,
    int FingerprintCount,
    int FaceCount,
    int AttendanceRecordCount,
    int UserCapacity,
    int FingerprintCapacity,
    int FaceCapacity,
    int AttendanceRecordCapacity);

public sealed record UpdateDeviceDto(
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    DeviceBrand Brand,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? Username = null,
    string? Password = null);

// DTOs de Descargas
public sealed record DownloadResultDto(
    string DeviceId,
    int RecordsDownloaded,
    DateTime DownloadedAt,
    DateTime? MinDate = null,
    DateTime? MaxDate = null,
    bool Success = true,
    string? ErrorMessage = null,
    List<string>? AffectedEmployeeIds = null);

public sealed record BulkDownloadResultDto(
    int TotalDevices,
    int SuccessfulDownloads,
    int FailedDownloads,
    int TotalRecordsDownloaded,
    List<DownloadResultDto> DeviceResults);

// DTOs de Reportes
public sealed record DailyAttendanceReportDto(
    DateOnly Date,
    List<EmployeeAttendanceSummaryDto> EmployeeSummaries);

public sealed record EmployeeAttendanceSummaryDto(
    string EmployeeId,
    string EmployeeName,
    DateTime? CheckIn,
    DateTime? CheckOut,
    TimeSpan? WorkedHours,
    bool IsLate,
    bool IsAbsent,
    int TotalChecks);

public sealed record AttendanceRangeQueryDto(
    string? EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? DeviceId);

// DTOs para queries
public sealed record GetEmployeeAttendanceDto(
    string EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record GetDeviceAttendanceDto(
    string DeviceId,
    DateOnly Date);

// DTOs de respuesta paginada
public sealed record PagedResultDto<T>(
    List<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages)
{
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

// DTOs de configuración
public sealed record SystemConfigurationDto(
    string CompanyName,
    byte[]? CompanyLogo,
    TimeSpan LateToleranceMinutes,
    TimeSpan StandardWorkHours,
    bool AutoClearDevicesAfterDownload,
    bool IsAutoDownloadEnabled,
    TimeSpan? AutoDownloadTime,
    bool AutoDownloadOnlyToday,
    int AdmsPort = 16373,
    string BackupDirectory = "Backups",
    int BackupTimeoutMinutes = 10,
    WorkPeriodMode WorkPeriodMode = WorkPeriodMode.Weekly,
    DayOfWeek WeeklyStartDay = DayOfWeek.Monday,
    int FortnightFirstDay = 1,
    int FortnightSecondDay = 16,
    int MonthlyStartDay = 1,
    bool AreAlertsEnabled = false,
    string? AbsenceAlertEmails = null,
    string? LateAlertEmails = null,
    string? SystemFailureAlertEmails = null,
    string? SmtpHost = null,
    int SmtpPort = 587,
    string? SmtpUser = null,
    string? SmtpPassword = null,
    bool SmtpEnableSsl = true,
    bool IsAutoBackupEnabled = false,
    TimeSpan? AutoBackupTime = null,
    bool IsAutoReportEnabled = false,
    TimeSpan? AutoReportTime = null,
    string? AutoReportEmails = null,
    bool AutoReportForToday = false);

// DTO para datos crudos del dispositivo ZKTeco
public sealed record RawAttendanceRecord(
    string UserId,
    DateTime CheckTime,
    int VerifyMethod,
    int InOutMode,
    int WorkCode);

public record DeviceFingerprintDto(int Index, string Template);

public sealed record DeviceUserDto(
    string UserId,
    string Name,
    string Password,
    int Privilege,
    bool Enabled,
    string? CardNumber = null,
    List<DeviceFingerprintDto>? Fingerprints = null,
    string? FaceTemplate = null,
    string? Photo = null);
