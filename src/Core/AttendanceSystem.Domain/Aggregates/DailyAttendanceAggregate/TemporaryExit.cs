namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

/// <summary>
/// Caso 11: una ausencia intermedia de la jornada (salida y regreso, en hora civil). RH la clasifica por separado;
/// <see cref="IsAutomaticLunch"/> indica que la ventana de comida la clasificó como comida.
/// </summary>
public sealed record TemporaryExit(
    Guid ExitRecordId,
    DateTime ExitTime,
    DateTime ReturnTime,
    int Minutes,
    TemporaryExitStatus Status,
    bool IsAutomaticLunch = false)
{
    /// <summary>Clasificación que tiene la salida sin decisión de RH.</summary>
    public TemporaryExitStatus AutomaticStatus => IsAutomaticLunch ? TemporaryExitStatus.Lunch : TemporaryExitStatus.Pending;

    /// <summary>Se descuenta completa: pendiente o permiso sin goce. De la comida sólo se descuenta el excedente.</summary>
    public bool IsDeducted => Status is TemporaryExitStatus.Pending or TemporaryExitStatus.ApprovedUnpaid;
}
