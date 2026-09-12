using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class WorkflowStageConfiguration : IEntityTypeConfiguration<WorkflowStage>
{
    public void Configure(EntityTypeBuilder<WorkflowStage> builder)
    {
        builder.ToTable("WorkflowStages");
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.ProjectId, s.Order }).IsUnique();

        // No navigation back to Project - same "reference by id only" pattern used for
        // Commit/Review/Conflict on Ticket.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Agents are only ever deactivated, never deleted (see AgentConfiguration), so this
        // relationship is never actually exercised by a delete - Restrict just keeps it honest.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(s => s.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing loop-back target. SQL Server disallows cascade delete on a
        // self-referencing FK, and WorkflowService already refuses to remove a stage another
        // stage's loop-back still targets, so this is never expected to fire - NoAction just
        // keeps the constraint from being rejected at model-build time.
        builder.HasOne<WorkflowStage>()
            .WithMany()
            .HasForeignKey(s => s.LoopBackToStageId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
