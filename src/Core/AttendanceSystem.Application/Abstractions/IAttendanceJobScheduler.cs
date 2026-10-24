namespace AttendanceSystem.Application.Abstractions;

public interface IAttendanceJobScheduler
{
    void ScheduleAutoDownload(TimeSpan timeOfDay);
    void DisableAutoDownload();
    void ScheduleCriticalAbsenceCheck();
    void ScheduleAutoBackup(TimeSpan timeOfDay);
    void DisableAutoBackup();
    void ScheduleAutoReport(TimeSpan timeOfDay);
    void DisableAutoReport();
    void ScheduleDeviceHeartbeat();
    void ScheduleCloudSync();
    /// <summary>Caso 10: extiende los rosters indefinidos y revisa que ningún día quede sin turno (cada hora y al iniciar).</summary>
    void ScheduleScheduleIntegrityCheck();
    /// <summary>Caso 15: al iniciar cada día calcula el día anterior de todos los empleados activos (faltas incluidas).</summary>
    void ScheduleDailyCalculation();
    void EnqueueBiometricSync(string deviceId, string employeeId);
}
