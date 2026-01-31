# PostgreSQL DateTime UTC Fix

## Problem
The application was throwing a `DbUpdateException` when trying to save `Employee` entities to PostgreSQL:

```
System.ArgumentException: Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone', only UTC is supported.
```

## Root Cause
PostgreSQL's `timestamp with time zone` data type requires DateTime values to be in UTC format. By default, .NET applications often create DateTime values with `DateTimeKind.Local`, which Npgsql (the PostgreSQL provider for Entity Framework Core) rejects.

## Solution
Configured Npgsql to automatically convert DateTime values to UTC before saving to the database by adding the following configuration in `Program.cs`:

```csharp
// Configurar Npgsql para convertir DateTime a UTC automáticamente
// Esto es necesario porque PostgreSQL requiere UTC para 'timestamp with time zone'
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);
```

### What This Does
- **`EnableLegacyTimestampBehavior = false`**: This tells Npgsql to use the modern timestamp behavior where:
  - All DateTime values are automatically converted to UTC before being sent to PostgreSQL
  - DateTime values read from the database are returned as UTC (DateTimeKind.Utc)
  - This ensures compatibility with PostgreSQL's `timestamp with time zone` type

## Impact
- **All DateTime properties** in your entities will now be automatically converted to UTC when saving
- **All DateTime values** read from the database will have `DateTimeKind.Utc`
- This applies to all entities including:
  - `Employee.HireDate`
  - `DownloadLog.StartedAt`, `CompletedAt`, `FromDate`, `ToDate`
  - `AttendanceRecord` timestamps
  - Any other DateTime properties

## Best Practices Going Forward
1. **Always use UTC in the database**: This is now enforced automatically
2. **Convert to local time in the UI layer**: When displaying dates to users, convert from UTC to the user's local timezone in the Blazor components
3. **Use `DateTime.UtcNow`**: When creating new DateTime values in your code, prefer `DateTime.UtcNow` over `DateTime.Now`
4. **Timezone handling**: If you need to work with specific timezones, use `TimeZoneInfo` to convert between UTC and local times

## Example: Displaying Local Time in Blazor
```csharp
@code {
    private DateTime utcDate = employee.HireDate; // This is UTC from database
    
    private string GetLocalDateString()
    {
        // Convert UTC to local time for display
        var localDate = utcDate.ToLocalTime();
        return localDate.ToString("dd/MM/yyyy");
    }
}
```

## References
- [Npgsql DateTime Documentation](https://www.npgsql.org/doc/types/datetime.html)
- [PostgreSQL Timestamp Types](https://www.postgresql.org/docs/current/datatype-datetime.html)
