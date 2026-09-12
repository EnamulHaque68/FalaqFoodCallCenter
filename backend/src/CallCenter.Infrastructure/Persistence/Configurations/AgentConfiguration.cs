using CallCenter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Infrastructure.Persistence.Configurations;

public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Team).HasMaxLength(100);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.EmployeeCode).IsUnique();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.User)
            .WithOne(x => x.Agent)
            .HasForeignKey<Agent>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.AssignedCalls)
            .WithOne(x => x.AssignedAgent)
            .HasForeignKey(x => x.AssignedAgentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.CallEvents)
            .WithOne(x => x.Agent)
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
