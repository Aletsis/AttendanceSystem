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
            var roles = AttendanceSystem.Application.Authorization.AppRoles.All;
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
                logger.LogInformation("Creando usuario administrador inicial...");

                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = "admin@attendance.com",
                    FullName = "Administrador del Sistema",
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var config = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var initialPassword = config?["InitialAdmin:Password"] ?? Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD");
                bool isGenerated = false;

                if (string.IsNullOrWhiteSpace(initialPassword))
                {
                    initialPassword = GenerateSecurePassword();
                    isGenerated = true;
                }

                var result = await userManager.CreateAsync(adminUser, initialPassword);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Administrador");
                    logger.LogInformation("Usuario administrador inicial ('{AdminEmail}') creado exitosamente.", adminEmail);
                    if (isGenerated)
                    {
                        var credentialsPath = Path.Combine(AppContext.BaseDirectory, ".initial_admin_credentials.txt");
                        await File.WriteAllTextAsync(credentialsPath, $"Usuario: {adminEmail}\nContraseña: {initialPassword}\nFecha: {DateTime.UtcNow:o}\nElimine este archivo después de configurar su cuenta.\n");
                        logger.LogWarning("⚠️ Se ha generado una contraseña temporal para '{AdminEmail}' y se ha guardado en '{Path}'. Por favor inicie sesión y cámbiela inmediatamente.", adminEmail, credentialsPath);
                    }
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

    private static string GenerateSecurePassword(int length = 16)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*()_+-=";
        const string all = upper + lower + digits + special;

        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        chars[0] = upper[bytes[0] % upper.Length];
        chars[1] = lower[bytes[1] % lower.Length];
        chars[2] = digits[bytes[2] % digits.Length];
        chars[3] = special[bytes[3] % special.Length];

        for (int i = 4; i < length; i++)
        {
            chars[i] = all[bytes[i] % all.Length];
        }

        var rand = new Random(BitConverter.ToInt32(bytes, 0));
        return new string(chars.OrderBy(_ => rand.Next()).ToArray());
    }
}
