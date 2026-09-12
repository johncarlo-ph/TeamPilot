using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class StageExecutionConfiguration : IEntityTypeConfiguration<StageExecution>
{
    public void Configure(EntityTypeBuilder<StageExecution> builder)
    {
        builder.ToTable("StageExecutions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Output).IsRequired().HasColumnType("nvarchar(max)");

        builder.HasIndex(e => new { e.TicketId, e.AgentId, e.CreatedAtUtc });

        // No navigation back to Ticket - same "reference by id only" pattern used for
        // Commit/Review/Conflict.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matches TicketAgentAssignment.AgentId exactly: agents are only ever deactivated, never
        // deleted, once they have real history - see Agent.HasCompleteInstructions and
        // WorkflowService.DeleteCustomAgentAsync's HasAssignmentHistoryAsync guard.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
