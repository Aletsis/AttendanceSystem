using System.Collections.Concurrent;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// F06-P03 / H22: Administrador y pool de NpgsqlDataSource para bases de datos Cloud.
/// Reutiliza los pools de conexiones por ConnectionString evitando crear/destruir conexiones en cada registro.
/// </summary>
public interface ICloudDataSourceProvider
{
    NpgsqlDataSource GetDataSource(string connectionString);
    bool IsSchemaEnsured(string connectionString);
    void MarkSchemaEnsured(string connectionString);
}

public class CloudDataSourceProvider : ICloudDataSourceProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _dataSources = new();
    private readonly ConcurrentDictionary<string, bool> _schemaEnsured = new();

    public NpgsqlDataSource GetDataSource(string connectionString)
    {
        return _dataSources.GetOrAdd(connectionString, cs => NpgsqlDataSource.Create(cs));
    }

    public bool IsSchemaEnsured(string connectionString)
    {
        return _schemaEnsured.TryGetValue(connectionString, out var ensured) && ensured;
    }

    public void MarkSchemaEnsured(string connectionString)
    {
        _schemaEnsured[connectionString] = true;
    }

    public void Dispose()
    {
        foreach (var ds in _dataSources.Values)
        {
            try
            {
                ds.Dispose();
            }
            catch { }
        }
        _dataSources.Clear();
        _schemaEnsured.Clear();
        GC.SuppressFinalize(this);
    }
}
