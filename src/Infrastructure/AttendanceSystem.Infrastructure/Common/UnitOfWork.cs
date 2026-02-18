using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Common
{
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
    }
}
