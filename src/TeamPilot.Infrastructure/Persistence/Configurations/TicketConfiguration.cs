using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.AcceptanceCriteria).IsRequired().HasMaxLength(4000);
        builder.Property(t => t.BranchName).HasMaxLength(200);
        builder.Property(t => t.CancellationReason).HasMaxLength(1000);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // A branch belongs to at most one ticket per project - permanently, even once that
        // ticket is Done or Cancelled (a cancelled ticket's branch still carries its old,
        // possibly-abandoned commits, so letting a different ticket reclaim it would silently
        // mix that stale work into the new ticket's history). Filtered because most tickets
        // have no branch yet and SQL Server would otherwise only allow one NULL. The app-level
        // check in TicketService.LinkBranchAsync exists to fail with a clear message before
        // this ever fires - this index is the hard backstop, not the primary guard.
        builder.HasIndex(t => new { t.ProjectId, t.BranchName })
            .IsUnique()
            .HasFilter("[BranchName] IS NOT NULL");

        builder.HasIndex(t => t.SprintId);

        builder.Navigation(t => t.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Commits).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Reviews).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Conflicts).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Ticket has no navigation back to Project (WithOne() with no argument) - same
        // "reference by id only" pattern used for Commit/Review/Conflict below. ProjectId is
        // denormalized from Sprint.ProjectId (kept for access-guard checks and the branch-
        // uniqueness index above, both project-wide rather than per-sprint), so this FK is
        // NoAction rather than Cascade - Sprint's own FK below already cascades Project ->
        // Sprint -> Ticket, and SQL Server rejects a second cascade path to the same table.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Sprint>()
            .WithMany()
            .HasForeignKey(t => t.SprintId)
            .OnDelete(DeleteBehavior.Cascade);

        // These children have no navigation back to Ticket (WithOne() with no argument) -
        // the relationship is configured entirely from this, the principal, side.
        builder.HasMany(t => t.Assignments)
            .WithOne()
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Commits)
            .WithOne()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Reviews)
            .WithOne()
            .HasForeignKey(r => r.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Conflicts)
            .WithOne()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
