namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

public class AttendanceRecord : AggregateRoot<AttendanceRecordId>
{
    public required EmployeeId EmployeeId { get; set; }
    public required DeviceId DeviceId { get; set; }
    public required DateTime CheckTime { get; set; }
    public required VerifyMethod VerifyMethod { get; set; }
    public required CheckType CheckType { get; set; }
    public required AttendanceStatus Status { get; set; }

    // Constructor privado - solo se crea mediante Factory
    private AttendanceRecord() { }

    // Factory Method (parte del Aggregate)
    public static AttendanceRecord Create(
        EmployeeId employeeId,
        DeviceId deviceId,
        DateTime checkTime,
        VerifyMethod verifyMethod,
        CheckType checkType)
    {
        var record = new AttendanceRecord
        {
            Id = AttendanceRecordId.CreateNew(),
            EmployeeId = employeeId,
            DeviceId = deviceId,
            CheckTime = checkTime,
            VerifyMethod = verifyMethod,
            CheckType = checkType,
            Status = AttendanceStatus.Pending
        };

        // Regla de negocio: validar horario
        record.ValidateBusinessHours();
        
        // Levantar evento de dominio
        record.AddDomainEvent(new AttendanceRecordedEvent(
            record.Id,
            record.EmployeeId,
            record.CheckTime,
            record.CheckType));

        return record;
    }

    // Métodos de negocio (comportamiento del agregado)
    public void MarkAsProcessed()
    {
        // Allow idempotent calls or handle re-processing logic at handler level?
        // Current logic: throws if already processed. 
        // We will stick to the existing rule: explicit transition.
        if (Status == AttendanceStatus.Processed)
            return; // Idempotent is safer for re-processing logic where we might re-save

        Status = AttendanceStatus.Processed;
        
        AddDomainEvent(new AttendanceProcessedEvent(Id, EmployeeId, CheckTime));
    }

    public void ResetStatus()
    {
        Status = AttendanceStatus.Pending;
        // Optionally clear domain events or add a "Reset" event?
    }

    public void SetInferredType(CheckType type)
    {
        CheckType = type;
    }

    public void MarkAsAnomalous(string reason)
    {
        Status = AttendanceStatus.Anomalous;
        
        AddDomainEvent(new AttendanceAnomalyDetectedEvent(
            Id, EmployeeId, CheckTime, reason));
    }

    // Invariantes (reglas de negocio del dominio)
    private void ValidateBusinessHours()
    {
        var hour = CheckTime.Hour;
        
        // Ejemplo: alertar si registro fuera de horario laboral
        if (hour < 6 || hour > 22)
        {
            AddDomainEvent(new OutOfHoursCheckDetectedEvent(
                Id, EmployeeId, CheckTime));
        }
    }
}