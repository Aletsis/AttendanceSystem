using System.Net.Sockets;
using AttendanceSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// F03-06: clasificación de fallos de EF Core/Npgsql por SQLSTATE.
/// 23505 unicidad · 40001/40P01 conflicto concurrente · 55P03 espera de bloqueo · 57014 cancelación/tiempo ·
/// 08*/transitorios conexión · 22*/23* datos inválidos.
/// </summary>
public sealed class NpgsqlPersistenceFailureClassifier : IPersistenceFailureClassifier
{
    public PersistenceFailure Classify(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException pg:
                    return FromSqlState(pg);
                case DbUpdateConcurrencyException:
                    return new PersistenceFailure(PersistenceFailureKind.ConcurrencyConflict, null, current.Message);
                case OperationCanceledException:
                    return new PersistenceFailure(PersistenceFailureKind.Cancelled, null, current.Message);
                case TimeoutException:
                    return new PersistenceFailure(PersistenceFailureKind.Timeout, null, current.Message);
                case SocketException or IOException:
                    return new PersistenceFailure(PersistenceFailureKind.Connection, null, current.Message);
                case NpgsqlException npgsql when npgsql.IsTransient:
                    // Puede envolver un TimeoutException o un fallo de socket; se sigue buscando la causa concreta.
                    var inner = current.InnerException == null ? null : Classify(current.InnerException);
                    return inner is { Kind: not PersistenceFailureKind.Unknown }
                        ? inner
                        : new PersistenceFailure(PersistenceFailureKind.Connection, null, current.Message);
            }
        }

        return new PersistenceFailure(PersistenceFailureKind.Unknown, null, exception.Message);
    }

    private static PersistenceFailure FromSqlState(PostgresException pg)
    {
        var state = pg.SqlState;
        var kind = state switch
        {
            PostgresErrorCodes.UniqueViolation => PersistenceFailureKind.UniqueViolation,
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected => PersistenceFailureKind.ConcurrencyConflict,
            PostgresErrorCodes.LockNotAvailable => PersistenceFailureKind.LockTimeout,
            PostgresErrorCodes.QueryCanceled => PersistenceFailureKind.Timeout,
            _ when state.StartsWith("08", StringComparison.Ordinal) => PersistenceFailureKind.Connection,
            PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown or PostgresErrorCodes.CannotConnectNow => PersistenceFailureKind.Connection,
            _ when state.StartsWith("22", StringComparison.Ordinal) || state.StartsWith("23", StringComparison.Ordinal) => PersistenceFailureKind.InvalidData,
            _ => PersistenceFailureKind.Unknown
        };

        return new PersistenceFailure(kind, pg.ConstraintName, pg.MessageText);
    }
}
