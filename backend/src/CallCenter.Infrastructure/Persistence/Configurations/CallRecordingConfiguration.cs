using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallRecordingConfiguration : IEntityTypeConfiguration<CallRecording>
{
    public void Configure(EntityTypeBuilder<CallRecording> builder)
    {
        builder.ToTable("CallRecordings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageUrl).HasMaxLength(1000);
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.StorageProvider).HasMaxLength(50);
        builder.Property(x => x.ProviderRecordingId).HasMaxLength(150);
        builder.Property(x => x.ContentType).HasMaxLength(100);
        builder.Property(x => x.ChecksumSha256).HasMaxLength(128);
        builder.Property(x => x.FileSizeBytes);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();

        builder.HasIndex(x => x.CallId);
        builder.HasIndex(x => x.ProviderRecordingId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RetentionUntil);
        builder.HasIndex(x => x.IsDeleted);
        builder.HasIndex(x => x.CreatedAt);

        builder.HasOne(x => x.Call)
            .WithMany(x => x.Recordings)
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
