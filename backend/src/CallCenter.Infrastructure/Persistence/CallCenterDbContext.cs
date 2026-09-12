using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Persistence;

public sealed class CallCenterDbContext(DbContextOptions<CallCenterDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Call> Calls => Set<Call>();
    public DbSet<CallEvent> CallEvents => Set<CallEvent>();
    public DbSet<CallQueue> CallQueues => Set<CallQueue>();
    public DbSet<CallQueueEntry> CallQueueEntries => Set<CallQueueEntry>();
    public DbSet<CallRecording> CallRecordings => Set<CallRecording>();
    public DbSet<CallDisposition> CallDispositions => Set<CallDisposition>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CallCenterDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
