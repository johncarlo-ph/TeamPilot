using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Content).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(m => m.ProposedTicketTitle).HasMaxLength(200);
        builder.Property(m => m.ProposedTicketDescription).HasColumnType("nvarchar(max)");
        builder.Property(m => m.SenderName).HasMaxLength(200);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });

        // No navigation back from Ticket - same "reference by id only" pattern used for
        // Commit/Review/Conflict/TicketQuestion. Restrict, matching TicketQuestion.AgentId:
        // tickets are only ever cancelled, never deleted, once they exist.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(m => m.CreatedTicketId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
