namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Categoría de un fallo de persistencia (F03-06). Sólo <see cref="UniqueViolation"/> sobre la
/// identidad del marcaje puede contarse como duplicado, y únicamente tras confirmar que la fila existe.
/// </summary>
public enum PersistenceFailureKind
{
    Unknown = 0,
    UniqueViolation = 1,
    ConcurrencyConflict = 2,
    Connection = 3,
    Timeout = 4,
    LockTimeout = 5,
    Cancelled = 6,
    InvalidData = 7
}

public sealed record PersistenceFailure(PersistenceFailureKind Kind, string? ConstraintName, string Message)
{
    /// <summary>Fallos que pueden reintentarse más tarde sin cambiar los datos de entrada.</summary>
    public bool IsTransient => Kind is PersistenceFailureKind.Connection
        or PersistenceFailureKind.Timeout
        or PersistenceFailureKind.LockTimeout
        or PersistenceFailureKind.ConcurrencyConflict;
}

public interface IPersistenceFailureClassifier
{
    PersistenceFailure Classify(Exception exception);
}

/// <summary>
/// Clasificador sin conocimiento del proveedor: reconoce cancelación y tiempo de espera; el resto es
/// <see cref="PersistenceFailureKind.Unknown"/> (nunca duplicado). Infraestructura registra el de PostgreSQL.
/// </summary>
public sealed class DefaultPersistenceFailureClassifier : IPersistenceFailureClassifier
{
    public static readonly IPersistenceFailureClassifier Instance = new DefaultPersistenceFailureClassifier();

    public PersistenceFailure Classify(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
                return new PersistenceFailure(PersistenceFailureKind.Cancelled, null, current.Message);
            if (current is TimeoutException)
                return new PersistenceFailure(PersistenceFailureKind.Timeout, null, current.Message);
        }

        return new PersistenceFailure(PersistenceFailureKind.Unknown, null, exception.Message);
    }
}
