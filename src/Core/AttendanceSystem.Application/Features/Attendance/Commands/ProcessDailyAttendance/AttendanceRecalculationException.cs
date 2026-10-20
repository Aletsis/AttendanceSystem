namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

public sealed record AttendanceRecalculationUnitFailure(string EmployeeId, string ErrorType, string Message);

/// <summary>
/// F03-04 / F00-07: un recálculo de varias unidades (empleados) donde alguna falló. Las unidades exitosas
/// quedaron confirmadas; las fallidas conservan íntegros sus resultados anteriores. Nunca se reporta
/// éxito total ante fallos parciales.
/// </summary>
public sealed class AttendanceRecalculationException : Exception
{
    public int TotalUnits { get; }
    public int SuccessfulUnits { get; }
    public IReadOnlyList<AttendanceRecalculationUnitFailure> FailedUnits { get; }

    public AttendanceRecalculationException(int totalUnits, int successfulUnits, IReadOnlyList<AttendanceRecalculationUnitFailure> failedUnits, Exception? firstFailure)
        : base($"Recálculo parcial: {successfulUnits} de {totalUnits} empleados confirmados; con fallo: " +
               string.Join(", ", failedUnits.Select(f => $"{f.EmployeeId} ({f.ErrorType}: {f.Message})")), firstFailure)
    {
        TotalUnits = totalUnits;
        SuccessfulUnits = successfulUnits;
        FailedUnits = failedUnits;
    }
}
