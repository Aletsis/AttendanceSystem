namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Las tareas con hora del día reciben la zona horaria del sistema (IANA): la hora configurada es la de la empresa, no
/// la del servidor. Las de horario configurable se programan con <c>ScheduledJobsSynchronizer</c>.
/// </summary>
public interface IAttendanceJobScheduler
{
    void ScheduleAutoDownload(TimeSpan timeOfDay, string timeZoneId);
    void DisableAutoDownload();
    void ScheduleCriticalAbsenceCheck();
    void ScheduleAutoBackup(TimeSpan timeOfDay, string timeZoneId);
    void DisableAutoBackup();
    void ScheduleAutoReport(TimeSpan timeOfDay, string timeZoneId);
    void DisableAutoReport();
    void ScheduleDeviceHeartbeat();
    void ScheduleCloudSync();
    /// <summary>Caso 10: extiende los rosters indefinidos y revisa que ningún día quede sin turno (cada hora y al iniciar).</summary>
    void ScheduleScheduleIntegrityCheck();
    /// <summary>
    /// Caso 15: al iniciar cada día calcula el día anterior de todos los empleados activos (faltas incluidas). Caso 17:
    /// después cierra solos los periodos que terminaron hace más de 3 días.
    /// </summary>
    void ScheduleDailyCalculation(string timeZoneId);
    /// <summary>Una sola sincronización de biometría por reloj para todos los empleados que no la tienen.</summary>
    void EnqueueBiometricSync(string deviceId, IReadOnlyCollection<string> employeeIds);
    /// <summary>Lo mismo para empleados externos sin huellas, por su ID en el reloj (código de sucursal + número).</summary>
    void EnqueueExternalBiometricSync(string deviceId, IReadOnlyCollection<string> deviceUserIds);
    /// <summary>
    /// Descarga de respaldo de un reloj escuchado en tiempo real (al reconectarse su escucha o si no se pudo guardar un
    /// marcaje): recupera lo marcado mientras la escucha no lo recibió.
    /// </summary>
    void EnqueueDeviceCatchUp(string deviceId);
}
