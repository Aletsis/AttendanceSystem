using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class SystemConfigurationRepository : ISystemConfigurationRepository
{
    private readonly AttendanceDbContext _context;

    public SystemConfigurationRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<SystemConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<SystemConfiguration>()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(SystemConfiguration configuration)
    {
        _context.Set<SystemConfiguration>().Add(configuration);
    }

    public void Update(SystemConfiguration configuration)
    {
        _context.Set<SystemConfiguration>().Update(configuration);
    }
}
