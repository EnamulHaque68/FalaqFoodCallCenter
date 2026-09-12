using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Tests;

public static class TestDbContextFactory
{
    public static CallCenterDbContext Create()
    {
        var options = new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CallCenterDbContext(options);
    }
}
