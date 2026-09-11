using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.Detail).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(64);

        // The admin listing always orders by CreatedAtUtc descending; index it so that stays
        // fast as the table grows.
        builder.HasIndex(a => a.CreatedAtUtc);

        // SetNull, not Cascade - audit history should outlive a deleted user. Defensive only:
        // this pass never deletes users.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
