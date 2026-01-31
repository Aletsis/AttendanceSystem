# Autenticación en AttendanceSystem

## Descripción General

El sistema de asistencia ahora cuenta con autenticación basada en **ASP.NET Core Identity**, que protege todas las páginas de la aplicación y requiere que los usuarios inicien sesión antes de acceder.

## Características Implementadas

### 1. **Sistema de Autenticación**
- Autenticación basada en cookies con ASP.NET Core Identity
- Sesiones con expiración configurable (8 horas por defecto)
- Revalidación automática del estado de autenticación cada 30 minutos
- Protección de todas las rutas excepto la página de login

### 2. **Gestión de Usuarios**
- Entidad `ApplicationUser` que extiende `IdentityUser`
- Campos adicionales:
  - `FullName`: Nombre completo del usuario
  - `IsActive`: Estado activo/inactivo del usuario
  - `CreatedAt`: Fecha de creación
  - `LastModifiedAt`: Fecha de última modificación

### 3. **Roles**
- **Administrador**: Acceso completo al sistema
- **Usuario**: Acceso estándar (para futuras implementaciones de permisos)

### 4. **Páginas de Autenticación**
- **Login** (`/login`): Página de inicio de sesión con diseño moderno usando MudBlazor
- **Logout** (`/logout`): Cierre de sesión automático

### 5. **Interfaz de Usuario**
- Indicador de usuario autenticado en la barra superior
- Botón de cerrar sesión visible en todo momento
- Redirección automática a login para usuarios no autenticados
- Soporte para "ReturnUrl" después del login

## Credenciales por Defecto

Al iniciar la aplicación por primera vez, se crea automáticamente un usuario administrador:

```
Usuario: admin
Contraseña: Admin123!
```

**⚠️ IMPORTANTE**: Por seguridad, se recomienda cambiar esta contraseña inmediatamente después del primer inicio de sesión.

## Requisitos de Contraseña

Las contraseñas deben cumplir con los siguientes requisitos:
- Mínimo 6 caracteres
- Al menos una letra mayúscula
- Al menos una letra minúscula
- Al menos un dígito
- Caracteres especiales opcionales

## Configuración

### Tiempo de Sesión

El tiempo de expiración de la sesión se puede configurar en `Program.cs`:

```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromHours(8); // Cambiar aquí
    options.SlidingExpiration = true;
});
```

### Intervalo de Revalidación

El intervalo de revalidación del estado de autenticación se configura en `IdentityRevalidatingAuthenticationStateProvider.cs`:

```csharp
protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30); // Cambiar aquí
```

## Tablas de Base de Datos

La migración `AddIdentityTables` crea las siguientes tablas en la base de datos:

- `AspNetUsers`: Usuarios del sistema
- `AspNetRoles`: Roles disponibles
- `AspNetUserRoles`: Relación usuarios-roles
- `AspNetUserClaims`: Claims de usuarios
- `AspNetUserLogins`: Logins externos (para futuras integraciones)
- `AspNetUserTokens`: Tokens de usuario
- `AspNetRoleClaims`: Claims de roles

## Flujo de Autenticación

1. Usuario intenta acceder a cualquier página del sistema
2. Si no está autenticado, es redirigido a `/login`
3. Ingresa sus credenciales
4. Si son correctas, se crea una cookie de autenticación
5. Usuario es redirigido a la página que intentaba acceder originalmente
6. La sesión se mantiene activa mientras el usuario interactúa con el sistema
7. El sistema revalida periódicamente que el usuario sigue activo y válido

## Próximos Pasos Recomendados

1. **Gestión de Usuarios**: Crear una interfaz para administrar usuarios (crear, editar, desactivar)
2. **Cambio de Contraseña**: Implementar funcionalidad para que los usuarios cambien su contraseña
3. **Recuperación de Contraseña**: Añadir flujo de "olvidé mi contraseña"
4. **Permisos Granulares**: Implementar autorización basada en roles para diferentes secciones
5. **Auditoría**: Registrar intentos de login exitosos y fallidos
6. **Two-Factor Authentication**: Añadir autenticación de dos factores para mayor seguridad

## Archivos Modificados/Creados

### Nuevos Archivos
- `src/Core/AttendanceSystem.Domain/Entities/ApplicationUser.cs`
- `src/Presentation/AttendanceSystem.Blazor.Server/Components/Pages/Login.razor`
- `src/Presentation/AttendanceSystem.Blazor.Server/Components/Pages/Logout.razor`
- `src/Presentation/AttendanceSystem.Blazor.Server/Components/RedirectToLogin.razor`
- `src/Presentation/AttendanceSystem.Blazor.Server/Services/IdentityRevalidatingAuthenticationStateProvider.cs`
- `src/Presentation/AttendanceSystem.Blazor.Server/Data/IdentityDataSeeder.cs`

### Archivos Modificados
- `src/Infrastructure/AttendanceSystem.Infrastructure/Persistence/AttendanceDbContext.cs`
- `src/Presentation/AttendanceSystem.Blazor.Server/Program.cs`
- `src/Presentation/AttendanceSystem.Blazor.Server/Components/Routes.razor`
- `src/Presentation/AttendanceSystem.Blazor.Server/Components/Layout/MainLayout.razor`

### Migración
- `src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/[timestamp]_AddIdentityTables.cs`

## Soporte

Para cualquier problema relacionado con la autenticación, revisar los logs de Serilog que incluyen información detallada sobre intentos de login y errores de autenticación.
