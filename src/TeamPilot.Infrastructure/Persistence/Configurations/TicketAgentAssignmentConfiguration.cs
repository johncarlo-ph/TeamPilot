using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketAgentAssignmentConfiguration : IEntityTypeConfiguration<TicketAgentAssignment>
{
    public void Configure(EntityTypeBuilder<TicketAgentAssignment> builder)
    {
        builder.ToTable("TicketAgentAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.RoleAtAssignment).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(a => new { a.TicketId, a.AgentId }).IsUnique();

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(a => a.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
