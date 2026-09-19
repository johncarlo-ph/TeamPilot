using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketPipelineNoteConfiguration : IEntityTypeConfiguration<TicketPipelineNote>
{
    public void Configure(EntityTypeBuilder<TicketPipelineNote> builder)
    {
        builder.ToTable("TicketPipelineNotes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Text).IsRequired().HasColumnType("nvarchar(max)");

        builder.HasIndex(n => new { n.TicketId, n.CreatedAtUtc });

        // No navigation back to Ticket - same "reference by id only" pattern used for
        // TicketProjectInstruction/TicketQuestion/StageExecution.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(n => n.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentId is nullable - matches TicketProjectInstruction/TicketQuestion/StageExecution:
        // agents are only ever deactivated, never deleted, once they have real history.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(n => n.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
