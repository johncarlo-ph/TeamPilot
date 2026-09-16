using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class ConflictConfiguration : IEntityTypeConfiguration<Conflict>
{
    public void Configure(EntityTypeBuilder<Conflict> builder)
    {
        builder.ToTable("Conflicts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FilePath).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.ConflictingDiffContent).HasColumnType("nvarchar(max)");
        builder.Property(c => c.AiSuggestedResolution).HasColumnType("nvarchar(max)");
        builder.Property(c => c.ResolvedContent).HasColumnType("nvarchar(max)");
        builder.Property(c => c.ResolutionNote).HasColumnType("nvarchar(max)");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.ResolvedBy).HasMaxLength(200);
        builder.Property(c => c.BaseTipSha).HasMaxLength(64);

        // NoAction (not SetNull/Cascade) - Ticket already cascade-deletes Conflicts directly,
        // and SQL Server rejects a second cascading path to the same table via Commits.
        builder.HasOne<Commit>()
            .WithMany()
            .HasForeignKey(c => c.CommitId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
