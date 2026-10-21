using System.Text;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// F03-02 / D-04: exclusión por empleado entre procesos (Blazor, WPF, Hangfire) mediante
/// <c>pg_advisory_xact_lock(espacio, clave)</c> en la transacción activa del contexto.
/// <list type="bullet">
/// <item>Clave: <c>(0x41545444 'ATTD', FNV-1a 32 bits del EmployeeId)</c>, estable entre procesos y plataformas.
/// Una colisión de hash sólo serializa a dos empleados; nunca permite concurrencia indebida.</item>
/// <item>Orden de adquisición: clave ascendente, sin duplicados, para evitar interbloqueos.</item>
/// <item>Espera máxima: <c>lock_timeout</c> local a la transacción (por defecto 30 s); al agotarse PostgreSQL
/// devuelve 55P03, que se clasifica como <see cref="PersistenceFailureKind.LockTimeout"/>.</item>
/// <item>Cancelación: Npgsql cancela la espera en el servidor; la transacción queda abortada y se revierte.</item>
/// <item>Liberación: automática al confirmar, revertir o perder la conexión (también si el proceso muere).</item>
/// </list>
/// </summary>
public sealed class PostgresAttendanceCalculationLockService : IAttendanceCalculationLockService
{
    public const int LockNamespace = 0x41545444;

    private readonly AttendanceDbContext _context;
    private readonly ILogger<PostgresAttendanceCalculationLockService> _logger;
    private readonly TimeSpan _timeout;

    public PostgresAttendanceCalculationLockService(
        AttendanceDbContext context,
        ILogger<PostgresAttendanceCalculationLockService> logger,
        TimeSpan? timeout = null)
    {
        _context = context;
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task AcquireAsync(IEnumerable<EmployeeId> employeeIds, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsNpgsql())
        {
            // Proveedores sin PostgreSQL (InMemory en pruebas) no comparten base entre procesos.
            return;
        }

        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Los bloqueos de asistencia requieren una transacción activa (pg_advisory_xact_lock).");
        }

        var keys = employeeIds
            .Select(e => LockKey(e.Value))
            .Distinct()
            .OrderBy(k => k)
            .ToList();
        if (keys.Count == 0) return;

        var waitStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timeoutMs = Math.Max(1, (int)_timeout.TotalMilliseconds);
        // F07-04: un solo viaje para todo el lote. Las sentencias se ejecutan en orden (clave ascendente), cada
        // espera respeta el lock_timeout local y la primera que lo agote aborta la transacción (55P03).
        // Las claves son enteros calculados aquí (no entrada externa): se escriben como literales.
        var sql = new StringBuilder();
        sql.Append("SET LOCAL lock_timeout = ").Append(timeoutMs).Append(';');
        foreach (var key in keys)
        {
            sql.Append("SELECT pg_advisory_xact_lock(").Append(LockNamespace).Append(", ").Append(key).Append(");");
        }
        sql.Append("SET LOCAL lock_timeout TO DEFAULT;");
        await _context.Database.ExecuteSqlRawAsync(sql.ToString(), cancellationToken);
        AttendanceSystem.Application.Common.AttendanceTelemetry.LockWait.Record(waitStopwatch.Elapsed.TotalMilliseconds);
        _logger.LogDebug("Bloqueos de asistencia adquiridos: {Count}", keys.Count);
    }

    /// <summary>FNV-1a de 32 bits sobre UTF-8; determinista (a diferencia de string.GetHashCode).</summary>
    public static int LockKey(string employeeId)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(employeeId))
            {
                hash ^= b;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
