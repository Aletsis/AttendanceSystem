using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando una operación de escritura o acceso a base de datos es rechazada
/// porque el sistema se encuentra en modo de mantenimiento o restauración.
/// </summary>
public class DatabaseMaintenanceException : Exception
{
    public MaintenanceState State { get; }
    public string? Owner { get; }

    public DatabaseMaintenanceException(MaintenanceState state, string? owner = null, string? message = null)
        : base(message ?? $"La base de datos se encuentra en modo de mantenimiento ({state}){(string.IsNullOrWhiteSpace(owner) ? "" : $" por {owner}")}. Operación rechazada para proteger la integridad.")
    {
        State = state;
        Owner = owner;
    }
}
