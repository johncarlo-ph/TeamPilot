using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class PipelineRunConfiguration : IEntityTypeConfiguration<PipelineRun>
{
    public void Configure(EntityTypeBuilder<PipelineRun> builder)
    {
        builder.ToTable("PipelineRuns");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.TriggerReason).IsRequired().HasMaxLength(500);
        builder.Property(p => p.LogOutput).HasColumnType("nvarchar(max)");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(p => p.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction (not Cascade/SetNull) - Project already cascade-deletes both Tickets and
        // PipelineRuns directly; a second cascading path via Ticket hits the same SQL Server
        // "multiple cascade paths" error already fixed once for Conflict -> Commit.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(p => p.TicketId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
