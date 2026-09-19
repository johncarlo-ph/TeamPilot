using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketProjectInstructionConfiguration : IEntityTypeConfiguration<TicketProjectInstruction>
{
    public void Configure(EntityTypeBuilder<TicketProjectInstruction> builder)
    {
        builder.ToTable("TicketProjectInstructions");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Text).IsRequired().HasColumnType("nvarchar(max)");

        builder.HasIndex(i => new { i.TicketId, i.CreatedAtUtc });

        // No navigation back to Ticket - same "reference by id only" pattern used for
        // TicketQuestion/StageExecution.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(i => i.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(i => i.ReferencedProjectId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // AgentId is nullable - matches TicketQuestion/StageExecution: agents are only ever
        // deactivated, never deleted, once they have real history.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(i => i.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
