namespace AttendanceSystem.Infrastructure.Persistence;

/// <summary>
/// Migraciones que alguna versión instalada aplicó con un identificador que ya no existe en el código. No delatan una
/// base más nueva: su contenido sigue en otra migración, idempotente, que se aplica como pendiente.
/// </summary>
internal static class RetiredMigrations
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        // Se compiló e instaló antes de regenerarse como 20260423210254_AddOvertimeFlagsToDailyAttendance (ADD COLUMN IF NOT EXISTS).
        "20260423195140_AddOvertimeFlagsToDailyAttendance"
    };

    /// <summary>Migraciones aplicadas que esta versión no conoce, sin contar las retiradas.</summary>
    public static List<string> Unknown(IEnumerable<string> applied, IEnumerable<string> known) =>
        applied.Except(known).Where(id => !Ids.Contains(id)).ToList();
}
