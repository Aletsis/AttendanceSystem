using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Infrastructure.Persistence;

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
            // Las migraciones las aplica StartupMigrationService antes de sembrar.
            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

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
            var adminEmail = AttendanceSystem.Application.Authorization.BuiltInAdmin.UserName;
            var adminUser = await userManager.FindByNameAsync(adminEmail);

            if (adminUser == null)
            {
                logger.LogInformation("Creando usuario administrador inicial...");

                adminUser = NewAdmin();

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
                        var credentialsPath = await WriteCredentialsAsync(adminEmail, initialPassword);
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

    private static ApplicationUser NewAdmin() => new()
    {
        UserName = AttendanceSystem.Application.Authorization.BuiltInAdmin.UserName,
        Email = "admin@attendance.com",
        FullName = "Administrador del Sistema",
        EmailConfirmed = true,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task<string> WriteCredentialsAsync(string userName, string password)
    {
        var credentialsPath = Path.Combine(AppContext.BaseDirectory, CredentialsFileName);
        await File.WriteAllTextAsync(credentialsPath, $"Usuario: {userName}\nContraseña: {password}\nFecha: {DateTime.UtcNow:o}\nElimine este archivo después de configurar su cuenta.\n");
        return credentialsPath;
    }

    public const string CredentialsFileName = ".initial_admin_credentials.txt";

    /// <summary>
    /// Recuperación del administrador del sistema (se ejecuta en el servidor, fuera del sitio): le asigna una contraseña
    /// temporal nueva, lo reactiva, le devuelve el rol de administrador y lo desbloquea; si lo borraron, lo vuelve a crear.
    /// Revoca sus sesiones abiertas. Devuelve la contraseña temporal y la guarda en <see cref="CredentialsFileName"/>.
    /// </summary>
    public static async Task<(string UserName, string Password, string CredentialsPath)> ResetBuiltInAdminAsync(
        IServiceProvider serviceProvider, ILogger logger, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var adminRole = AttendanceSystem.Application.Authorization.AppRoles.Administrator;

        if (!await roleManager.RoleExistsAsync(adminRole))
            Check(await roleManager.CreateAsync(new IdentityRole(adminRole)), "crear el rol de administrador");

        var password = GenerateSecurePassword();
        var admin = await userManager.FindByNameAsync(AttendanceSystem.Application.Authorization.BuiltInAdmin.UserName);
        if (admin == null)
        {
            admin = NewAdmin();
            Check(await userManager.CreateAsync(admin, password), "crear el administrador");
            logger.LogWarning("El administrador del sistema no existía; se volvió a crear.");
        }
        else
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(admin);
            Check(await userManager.ResetPasswordAsync(admin, token, password), "restablecer la contraseña");
        }

        admin.IsActive = true;
        admin.LastModifiedAt = DateTime.UtcNow;
        Check(await userManager.UpdateAsync(admin), "reactivar el administrador");
        if (!await userManager.IsInRoleAsync(admin, adminRole))
            Check(await userManager.AddToRoleAsync(admin, adminRole), "asignar el rol de administrador");
        Check(await userManager.SetLockoutEndDateAsync(admin, null), "desbloquear el administrador");
        Check(await userManager.ResetAccessFailedCountAsync(admin), "reiniciar los intentos fallidos");
        Check(await userManager.UpdateSecurityStampAsync(admin), "cerrar sus sesiones abiertas");

        var path = await WriteCredentialsAsync(admin.UserName!, password);
        logger.LogWarning("Contraseña del administrador '{User}' restablecida; la temporal se guardó en '{Path}'.", admin.UserName, path);
        return (admin.UserName!, password, path);
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"No se pudo {action}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
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
