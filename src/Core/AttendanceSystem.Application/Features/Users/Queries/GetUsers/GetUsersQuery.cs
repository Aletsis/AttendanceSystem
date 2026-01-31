using MediatR;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Features.Users.Queries.GetUsers;

public sealed record GetUsersQuery : IRequest<List<UserDto>>;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, List<UserDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUsersQueryHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<List<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        // Nota: UserManager.Users devuelve IQueryable. Al usar ToList() se ejecuta la consulta.
        // En un escenario ideal, usaríamos ToListAsync() de EF Core, pero para evitar acoplamiento
        // directo a EF Core en la capa de Aplicación, usaremos la ejecución síncrona o podríamos
        // envolverlo en Task.Run si fuera crítico, aunque para una lista de usuarios administrativa está bien.
        var users = _userManager.Users.ToList();
        
        var userDtos = new List<UserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userDtos.Add(new UserDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                user.FullName,
                user.IsActive,
                roles.ToList()
            ));
        }

        return userDtos;
    }
}
