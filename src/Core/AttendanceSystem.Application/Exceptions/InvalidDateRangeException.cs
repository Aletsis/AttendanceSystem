namespace AttendanceSystem.Application.Exceptions;

/// <summary>F07-02 / A05: el rango de fechas de la petición no es válido; no se consultó ni se modificó nada.</summary>
public sealed class InvalidDateRangeException : Exception
{
    public InvalidDateRangeException(string message) : base(message) { }
}
