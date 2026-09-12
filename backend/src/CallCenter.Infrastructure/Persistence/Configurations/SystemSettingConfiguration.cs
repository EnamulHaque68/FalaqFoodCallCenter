using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired().HasDefaultValue("General");
        builder.Property(x => x.DataType).HasMaxLength(20).IsRequired().HasDefaultValue("String");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Key).IsUnique();
    }
}
