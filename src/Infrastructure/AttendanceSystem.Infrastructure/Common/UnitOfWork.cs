using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AttendanceSystem.Infrastructure.Common;

public class UnitOfWork : IUnitOfWork
{
    private readonly AttendanceDbContext _dbContext;

    public UnitOfWork(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Delega en el contexto para aplicar el mismo protocolo de outbox (publicar sólo tras el commit).
    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        _dbContext.BeginTransactionAsync(cancellationToken);

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        _dbContext.CommitTransactionAsync(cancellationToken);

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        _dbContext.RollbackTransactionAsync(cancellationToken);

    public void DiscardChanges() => _dbContext.DiscardChanges();
}
