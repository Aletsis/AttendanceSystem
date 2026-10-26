using System.Linq;
using AttendanceSystem.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Las migraciones se aplican solas al arrancar: un snapshot desactualizado o una migración nueva ordenada antes que
/// las existentes no se notan al compilar, pero rompen el arranque de todas las instalaciones. No requiere PostgreSQL.
/// </summary>
public class MigrationsConsistencyTests
{
    private static AttendanceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        return new AttendanceDbContext(options, new Moq.Mock<MediatR.IPublisher>().Object);
    }

    [Fact]
    public void Model_HasNoChangesMissingFromTheMigrations()
    {
        using var context = CreateContext();

        context.Database.HasPendingModelChanges().Should().BeFalse(
            "cada cambio del modelo necesita su migración: genérela con 'dotnet ef migrations add' en vez de escribirla sin snapshot");
    }

    [Fact]
    public void LastMigrationById_MatchesTheSnapshot()
    {
        using var context = CreateContext();
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();

        var lastId = migrationsAssembly.Migrations.Keys.Max()!;
        var lastMigration = migrationsAssembly.CreateMigration(migrationsAssembly.Migrations[lastId], context.Database.ProviderName!);

        lastMigration.TargetModel.Should().NotBeNull($"la última migración ({lastId}) debe guardar el modelo completo en su Designer");
        var differences = differ.GetDifferences(
            RelationalModel(lastMigration.TargetModel!, initializer),
            RelationalModel(migrationsAssembly.ModelSnapshot!.Model, initializer));

        differences.Should().BeEmpty(
            $"EF aplica las migraciones por orden de ID y la más reciente debe ser la última ({lastId}); " +
            "si una migración nueva quedó con un ID anterior, renómbrela con uno posterior a éste");
    }

    private static IRelationalModel RelationalModel(IModel model, IModelRuntimeInitializer initializer) =>
        initializer.Initialize(model is IMutableModel mutable ? mutable.FinalizeModel() : model).GetRelationalModel();
}
