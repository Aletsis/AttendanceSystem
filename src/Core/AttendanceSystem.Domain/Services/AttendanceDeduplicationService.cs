namespace AttendanceSystem.Domain.Services;

using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

public sealed class AttendanceDeduplicationService
{
    /// <summary>
    /// Filtra una lista de registros candidatos eliminando aquellos que ya existen en el conjunto de referencias.
    /// La unicidad se determina por la combinación de EmployeeId y CheckTime.
    /// </summary>
    public IReadOnlyList<AttendanceRecord> FilterNewRecords(
        IEnumerable<AttendanceRecord> candidates, 
        IEnumerable<AttendanceRecord> existing)
    {
         if (!candidates.Any())
            return Array.Empty<AttendanceRecord>();

        // 1. Deduplicar candidatos internamente (por si el dispositivo envía duplicados en el mismo lote)
        var distinctCandidates = candidates
            .DistinctBy(r => new { r.EmployeeId, r.CheckTime })
            .ToList();

        if (!existing.Any())
            return distinctCandidates;

        // 2. Crear HashSet de claves existentes para búsqueda O(1)
        var existingKeys = existing
            .Select(r => new { r.EmployeeId, r.CheckTime })
            .ToHashSet();

        // 3. Filtrar candidatos que no estén en el conjunto existente
        return distinctCandidates
            .Where(candidate => !existingKeys.Contains(new { candidate.EmployeeId, candidate.CheckTime }))
            .ToList();
    }
}
