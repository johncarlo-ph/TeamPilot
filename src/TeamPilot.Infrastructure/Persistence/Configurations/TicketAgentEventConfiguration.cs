using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketAgentEventConfiguration : IEntityTypeConfiguration<TicketAgentEvent>
{
    public void Configure(EntityTypeBuilder<TicketAgentEvent> builder)
    {
        builder.ToTable("TicketAgentEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Result).HasColumnType("nvarchar(max)");

        builder.HasIndex(e => new { e.TicketId, e.CreatedAtUtc });

        // No navigation back to Ticket - same "reference by id only" pattern used for
        // Commit/Review/Conflict/StageExecution/TicketQuestion.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentId is nullable (a pre-stage failure isn't tied to a specific agent) - Restrict,
        // matching StageExecution/TicketQuestion/TicketAgentAssignment: agents are only ever
        // deactivated, never deleted, once they have real history.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
