using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketQuestionConfiguration : IEntityTypeConfiguration<TicketQuestion>
{
    public void Configure(EntityTypeBuilder<TicketQuestion> builder)
    {
        builder.ToTable("TicketQuestions");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(q => q.Prompt).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(q => q.AnswerText).HasColumnType("nvarchar(max)");
        builder.Property(q => q.AnsweredBy).HasMaxLength(200);

        builder.HasIndex(q => new { q.TicketId, q.CreatedAtUtc });

        // No navigation back to Ticket - same "reference by id only" pattern used for
        // Commit/Review/Conflict/StageExecution.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(q => q.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentId is nullable (a failure isn't always tied to a specific stage) - an optional
        // relationship by default for a nullable FK, stated explicitly for clarity. Restrict,
        // matching StageExecution/TicketAgentAssignment: agents are only ever deactivated, never
        // deleted, once they have real history.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(q => q.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
