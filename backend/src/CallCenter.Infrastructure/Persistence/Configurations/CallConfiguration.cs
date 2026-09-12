using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallConfiguration : IEntityTypeConfiguration<Call>
{
    public void Configure(EntityTypeBuilder<Call> builder)
    {
        builder.ToTable("Calls");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderCallId).HasMaxLength(150);
        builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(150);
        builder.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.FollowUpAt);
        builder.Property(x => x.FollowUpNotes).HasMaxLength(1000);
        builder.Property(x => x.Direction).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.ProviderCallId);
        builder.HasIndex(x => x.CorrelationId).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.StartedAt, x.Direction, x.Status });
        builder.HasIndex(x => new { x.StartedAt, x.AssignedAgentId });
        builder.HasIndex(x => new { x.StartedAt, x.CallDispositionId });
        builder.HasIndex(x => new { x.AssignedAgentId, x.Status });
        builder.HasIndex(x => x.PhoneNumber);
        builder.HasIndex(x => new { x.CustomerId, x.StartedAt });
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        builder.HasIndex(x => new { x.CallQueueId, x.Status });
        builder.HasIndex(x => x.CallDispositionId);
        builder.HasIndex(x => x.FollowUpAt);

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Calls)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedAgent)
            .WithMany(x => x.AssignedCalls)
            .HasForeignKey(x => x.AssignedAgentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CallQueue)
            .WithMany(x => x.Calls)
            .HasForeignKey(x => x.CallQueueId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CallDisposition)
            .WithMany(x => x.Calls)
            .HasForeignKey(x => x.CallDispositionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
