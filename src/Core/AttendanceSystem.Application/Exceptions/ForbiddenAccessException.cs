namespace AttendanceSystem.Application.Exceptions;

/// <summary>Caso 13: el usuario no tiene el permiso que exige la petición; no se ejecutó nada.</summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message) { }
}
