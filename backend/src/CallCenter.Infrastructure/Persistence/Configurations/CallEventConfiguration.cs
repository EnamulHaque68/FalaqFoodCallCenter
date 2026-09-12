using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallEventConfiguration : IEntityTypeConfiguration<CallEvent>
{
    public void Configure(EntityTypeBuilder<CallEvent> builder)
    {
        builder.ToTable("CallEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => new { x.CallId, x.OccurredAt });
        builder.HasIndex(x => new { x.AgentId, x.OccurredAt });

        builder.HasOne(x => x.Call)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Agent)
            .WithMany(x => x.CallEvents)
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
