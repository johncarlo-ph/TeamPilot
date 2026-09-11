using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class CommitConfiguration : IEntityTypeConfiguration<Commit>
{
    public void Configure(EntityTypeBuilder<Commit> builder)
    {
        builder.ToTable("Commits");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.BranchName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CommitHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Message).IsRequired().HasMaxLength(500);
        builder.Property(c => c.DiffContent).HasColumnType("nvarchar(max)");

        // A hash is only meaningfully unique within its own ticket's branch history.
        builder.HasIndex(c => new { c.TicketId, c.CommitHash }).IsUnique();
    }
}
