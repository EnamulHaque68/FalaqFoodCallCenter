using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallQueueConfiguration : IEntityTypeConfiguration<CallQueue>
{
    public void Configure(EntityTypeBuilder<CallQueue> builder)
    {
        builder.ToTable("CallQueues");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Priority });
    }
}
