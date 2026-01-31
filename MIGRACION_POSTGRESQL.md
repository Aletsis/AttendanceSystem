# Migración a PostgreSQL - Sistema de Asistencia

## ✅ Cambios Realizados

Se ha completado la migración del sistema de **SQL Server** a **PostgreSQL**. Los siguientes cambios fueron aplicados:

### 1. Paquetes NuGet Actualizados

**Eliminados:**
- `Microsoft.EntityFrameworkCore.SqlServer` → Reemplazado por `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Serilog.Sinks.MSSqlServer` → Reemplazado por `Serilog.Sinks.PostgreSQL`

**Agregados:**
- `Npgsql.EntityFrameworkCore.PostgreSQL` (v9.0.2)
- `Serilog.Sinks.PostgreSQL` (v2.3.0)
- `Hangfire.PostgreSql` (v1.20.11)

### 2. Archivos Modificados

#### `AttendanceSystem.Infrastructure.csproj`
- Actualizado para usar paquetes de PostgreSQL

#### `Program.cs`
- Cambiado `UseSqlServer()` por `UseNpgsql()`
- Cambiado `using Hangfire.SqlServer` por `using Hangfire.PostgreSql`
- Actualizado Hangfire para usar `UsePostgreSqlStorage()`

#### `appsettings.json`
- Connection strings actualizados a formato PostgreSQL:
  ```json
  "ConnectionStrings": {
    "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=postgres;",
    "HangfireDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=postgres;Password=postgres;"
  }
  ```
- Configuración de Serilog actualizada para usar sink de PostgreSQL

#### `LoggingConfiguration.cs`
- Eliminadas referencias a `Serilog.Sinks.MSSqlServer`
- Simplificado para usar configuración desde `appsettings.json`

### 3. Migraciones
- Eliminadas todas las migraciones antiguas de SQL Server
- Creada nueva migración inicial: `InitialPostgreSQL`

### 4. Verificación Automática de Migraciones
- **Configurado en `Program.cs`**: La aplicación ahora verifica automáticamente al iniciar si hay migraciones pendientes
- **Aplicación automática**: Si se detectan migraciones pendientes, se aplican automáticamente antes de que la aplicación inicie
- **Logging detallado**: Se registran todas las migraciones pendientes y el resultado de su aplicación
- **Seguridad**: Si hay un error al aplicar migraciones, la aplicación no iniciará (fail-fast)
- **Beneficios**:
  - No es necesario ejecutar manualmente `dotnet ef database update`
  - Garantiza que la base de datos siempre esté actualizada
  - Previene errores por esquema de base de datos desactualizado
  - Ideal para despliegues en producción

## 📋 Pasos Siguientes

### 1. Instalar PostgreSQL

Si aún no tienes PostgreSQL instalado:

**Windows:**
```powershell
# Descargar desde: https://www.postgresql.org/download/windows/
# O usar Chocolatey:
choco install postgresql
```

**Verificar instalación:**
```powershell
psql --version
```

### 2. Configurar PostgreSQL

Asegúrate de que PostgreSQL esté corriendo:

```powershell
# Verificar servicio
Get-Service postgresql*

# Si no está corriendo, iniciarlo:
Start-Service postgresql-x64-15  # Ajusta el nombre según tu versión
```

### 3. Crear la Base de Datos

Conéctate a PostgreSQL y crea la base de datos:

```powershell
# Conectar a PostgreSQL (por defecto usa el usuario postgres)
psql -U postgres

# En el prompt de PostgreSQL:
CREATE DATABASE "AttendanceSystem";
\q
```

### 4. Actualizar Connection String (si es necesario)

Edita `appsettings.json` si tu configuración de PostgreSQL es diferente:

```json
"ConnectionStrings": {
  "AttendanceDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=TU_USUARIO;Password=TU_PASSWORD;",
  "HangfireDb": "Host=localhost;Port=5432;Database=AttendanceSystem;Username=TU_USUARIO;Password=TU_PASSWORD;"
}
```

### 5. Aplicar las Migraciones

**¡NOTA IMPORTANTE!** A partir de ahora, las migraciones se aplican **automáticamente** al iniciar la aplicación.

Ya **NO es necesario** ejecutar manualmente:
```powershell
# Este comando ya NO es necesario (pero aún funciona si lo prefieres)
dotnet ef database update --project src\Infrastructure\AttendanceSystem.Infrastructure --startup-project src\Presentation\AttendanceSystem.Blazor.Server
```

**Cómo funciona:**
1. Al iniciar la aplicación, se verifica automáticamente si hay migraciones pendientes
2. Si hay migraciones pendientes, se aplican automáticamente
3. Se registran logs detallados del proceso
4. Si hay algún error, la aplicación no iniciará (para evitar inconsistencias)

**Ventajas:**
- ✅ Más seguro: garantiza que la BD esté actualizada antes de usar la app
- ✅ Más conveniente: no necesitas recordar ejecutar comandos manualmente
- ✅ Mejor para producción: despliegues más confiables

**Si prefieres aplicar migraciones manualmente:**
Puedes seguir usando el comando tradicional de Entity Framework si lo prefieres.

### 6. Verificar la Migración

Conéctate a PostgreSQL y verifica que las tablas se crearon:

```powershell
psql -U postgres -d AttendanceSystem

# En el prompt de PostgreSQL:
\dt  # Listar todas las tablas
\q
```

### 7. Ejecutar la Aplicación

```powershell
dotnet run --project src\Presentation\AttendanceSystem.Blazor.Server
```

## 🔍 Verificaciones Importantes

### Verificar Hangfire
- Accede a `/hangfire` en tu navegador
- Verifica que Hangfire esté usando PostgreSQL correctamente

### Verificar Logs
- Los logs ahora se guardarán en la tabla `Logs` de PostgreSQL
- Verifica que los logs se estén escribiendo correctamente

### Verificar Identity
- Verifica que puedas iniciar sesión
- Las tablas de Identity (`AspNetUsers`, `AspNetRoles`, etc.) deben estar en PostgreSQL

## 🚨 Solución de Problemas

### Error: "No se puede conectar a PostgreSQL"
1. Verifica que PostgreSQL esté corriendo
2. Verifica el puerto (por defecto 5432)
3. Verifica usuario y contraseña en connection string

### Error: "La base de datos no existe"
```powershell
psql -U postgres
CREATE DATABASE "AttendanceSystem";
```

### Error: "Permisos insuficientes"
Asegúrate de que el usuario de PostgreSQL tenga permisos para crear tablas:
```sql
GRANT ALL PRIVILEGES ON DATABASE "AttendanceSystem" TO postgres;
```

### Error en Hangfire
Si Hangfire no funciona correctamente, verifica que las tablas de Hangfire se hayan creado:
```sql
SELECT * FROM information_schema.tables WHERE table_schema = 'public' AND table_name LIKE 'hangfire%';
```

## 📊 Diferencias entre SQL Server y PostgreSQL

### Tipos de Datos
- `nvarchar` → `text` o `varchar`
- `datetime2` → `timestamp`
- `bit` → `boolean`

### Nombres de Objetos
- PostgreSQL es case-sensitive para nombres entre comillas
- Se recomienda usar nombres en minúsculas

### Índices y Constraints
- La sintaxis puede variar ligeramente
- PostgreSQL usa secuencias en lugar de IDENTITY

## 🎯 Próximos Pasos Recomendados

1. **Backup de Datos**: Si tenías datos en SQL Server, necesitarás migrarlos
2. **Testing**: Prueba todas las funcionalidades del sistema
3. **Performance**: Ajusta índices según sea necesario
4. **Monitoreo**: Configura monitoreo de PostgreSQL

## 📝 Notas Adicionales

- **Desarrollo**: La configuración actual usa `localhost` y credenciales por defecto
- **Producción**: Asegúrate de usar credenciales seguras y conexiones SSL
- **Backup**: Configura backups automáticos de PostgreSQL
- **Migración de Datos**: Si necesitas migrar datos existentes de SQL Server, considera usar herramientas como `pgloader`

## 🔗 Recursos Útiles

- [Documentación de Npgsql](https://www.npgsql.org/efcore/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [Hangfire con PostgreSQL](https://github.com/frankhommers/Hangfire.PostgreSql)
- [Serilog PostgreSQL Sink](https://github.com/b00ted/serilog-sinks-postgresql)
