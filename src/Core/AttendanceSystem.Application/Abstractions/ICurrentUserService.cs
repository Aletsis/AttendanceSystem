namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Provee información sobre el usuario actualmente autenticado en la sesión o contexto de ejecución.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
}
