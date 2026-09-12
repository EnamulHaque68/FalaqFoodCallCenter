using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class CallDispositionConfiguration : IEntityTypeConfiguration<CallDisposition>
{
    public void Configure(EntityTypeBuilder<CallDisposition> builder)
    {
        builder.ToTable("CallDispositions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RequiresFollowUp).HasDefaultValue(false);
        builder.Property(x => x.RequiresNotes).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder, x.Name });
    }
}
