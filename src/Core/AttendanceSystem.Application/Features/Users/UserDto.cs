namespace AttendanceSystem.Application.Features.Users;

public sealed record UserDto(
    string Id,
    string UserName,
    string Email,
    string FullName,
    bool IsActive,
    List<string> Roles)
{
    public UserDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, true, new List<string>()) { }
}
