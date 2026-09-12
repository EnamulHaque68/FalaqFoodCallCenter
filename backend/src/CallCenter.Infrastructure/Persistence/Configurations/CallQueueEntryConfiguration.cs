using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallQueueEntryConfiguration : IEntityTypeConfiguration<CallQueueEntry>
{
    public void Configure(EntityTypeBuilder<CallQueueEntry> builder)
    {
        builder.ToTable("CallQueueEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Priority).HasDefaultValue(0);
        builder.HasIndex(x => new { x.CallQueueId, x.DequeuedAt, x.Position });
        builder.HasIndex(x => new { x.CallQueueId, x.DequeuedAt, x.Priority, x.Position });
        builder.HasIndex(x => new { x.CallId, x.DequeuedAt });
        builder.HasIndex(x => new { x.EnqueuedAt, x.CallQueueId, x.DequeuedAt });

        builder.HasOne(x => x.CallQueue)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.CallQueueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Call)
            .WithMany(x => x.QueueEntries)
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
