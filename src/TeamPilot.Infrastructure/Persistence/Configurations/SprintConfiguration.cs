using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprints");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.BaseBranch).IsRequired().HasMaxLength(200);
        builder.Property(s => s.SprintStartDate).HasColumnType("date");
        builder.Property(s => s.SprintEndDate).HasColumnType("date");
        builder.Property(s => s.SprintGoal).HasMaxLength(1000);

        // Sprint has no navigation back to Project (WithOne() with no argument) - same
        // "reference by id only" pattern used throughout (see TicketConfiguration).
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
