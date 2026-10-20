using System;
using System.IO;
using System.Net.Sockets;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

/// <summary>F03-06: clasificación por SQLSTATE y tipo de excepción.</summary>
public class NpgsqlPersistenceFailureClassifierTests
{
    private readonly NpgsqlPersistenceFailureClassifier _classifier = new();

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, PersistenceFailureKind.UniqueViolation)]
    [InlineData(PostgresErrorCodes.SerializationFailure, PersistenceFailureKind.ConcurrencyConflict)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, PersistenceFailureKind.ConcurrencyConflict)]
    [InlineData(PostgresErrorCodes.LockNotAvailable, PersistenceFailureKind.LockTimeout)]
    [InlineData(PostgresErrorCodes.QueryCanceled, PersistenceFailureKind.Timeout)]
    [InlineData(PostgresErrorCodes.ConnectionFailure, PersistenceFailureKind.Connection)]
    [InlineData(PostgresErrorCodes.AdminShutdown, PersistenceFailureKind.Connection)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, PersistenceFailureKind.InvalidData)]
    [InlineData(PostgresErrorCodes.CheckViolation, PersistenceFailureKind.InvalidData)]
    [InlineData(PostgresErrorCodes.InvalidTextRepresentation, PersistenceFailureKind.InvalidData)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege, PersistenceFailureKind.Unknown)]
    public void PostgresSqlState_IsMappedToItsKind(string sqlState, PersistenceFailureKind expected)
    {
        var pg = new PostgresException("simulado", "ERROR", "ERROR", sqlState);
        var wrapped = new DbUpdateException("guardado", pg);

        _classifier.Classify(wrapped).Kind.Should().Be(expected);
    }

    [Fact]
    public void ConnectionAndCancellationExceptions_AreNotDuplicates()
    {
        _classifier.Classify(new NpgsqlException("sin conexión", new SocketException())).Kind.Should().Be(PersistenceFailureKind.Connection);
        _classifier.Classify(new NpgsqlException("lectura", new IOException())).Kind.Should().Be(PersistenceFailureKind.Connection);
        _classifier.Classify(new NpgsqlException("espera", new TimeoutException())).Kind.Should().Be(PersistenceFailureKind.Timeout);
        _classifier.Classify(new OperationCanceledException()).Kind.Should().Be(PersistenceFailureKind.Cancelled);
        _classifier.Classify(new DbUpdateConcurrencyException("xmin")).Kind.Should().Be(PersistenceFailureKind.ConcurrencyConflict);
        _classifier.Classify(new InvalidOperationException("otro")).Kind.Should().Be(PersistenceFailureKind.Unknown);
    }

    [Fact]
    public void LockKey_IsStableAcrossProcesses()
    {
        // FNV-1a fijo: el mismo empleado produce la misma clave en Blazor, WPF y Hangfire.
        PostgresAttendanceCalculationLockService.LockKey("EMP-001").Should().Be(PostgresAttendanceCalculationLockService.LockKey("EMP-001"));
        PostgresAttendanceCalculationLockService.LockKey("").Should().Be(unchecked((int)2166136261));
        PostgresAttendanceCalculationLockService.LockKey("EMP-001").Should().NotBe(PostgresAttendanceCalculationLockService.LockKey("EMP-002"));
    }
}
