using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Infrastructure.Persistence;

namespace AttendanceSystem.Blazor.Server.Data;

/// <summary>
/// Inicializa datos de Identity en la base de datos
/// </summary>
public static class IdentityDataSeeder
{
    /// <summary>
    /// Inicializa roles y usuario administrador por defecto
    /// </summary>
    public static async Task SeedAsync(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Aplicar migraciones pendientes
            if ((await context.Database.GetPendingMigrationsAsync()).Any())
            {
                logger.LogInformation("Aplicando migraciones pendientes...");
                await context.Database.MigrateAsync();
                logger.LogInformation("Migraciones aplicadas exitosamente");
            }

            // Crear roles si no existen
            string[] roles = { "Administrador", "Usuario" };
            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    logger.LogInformation("Creando rol: {RoleName}", roleName);
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Crear usuario administrador por defecto si no existe
            var adminEmail = "admin";
            var adminUser = await userManager.FindByNameAsync(adminEmail);

            if (adminUser == null)
            {
                logger.LogInformation("Creando usuario administrador por defecto...");
                
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = "admin@attendance.com",
                    FullName = "Administrador del Sistema",
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(adminUser, "Admin123!");

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Administrador");
                    logger.LogInformation("Usuario administrador creado exitosamente");
                    logger.LogWarning("IMPORTANTE: Cambie la contraseña del administrador por defecto");
                    logger.LogInformation("Usuario: admin");
                    logger.LogInformation("Contraseña: Admin123!");
                }
                else
                {
                    logger.LogError("Error al crear usuario administrador: {Errors}", 
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al inicializar datos de Identity");
            throw;
        }
    }
}
