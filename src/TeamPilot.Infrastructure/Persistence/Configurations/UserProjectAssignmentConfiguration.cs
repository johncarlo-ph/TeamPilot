using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class UserProjectAssignmentConfiguration : IEntityTypeConfiguration<UserProjectAssignment>
{
    public void Configure(EntityTypeBuilder<UserProjectAssignment> builder)
    {
        builder.ToTable("UserProjectAssignments");
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.UserId, a.ProjectId }).IsUnique();

        // Users and Projects are independent parents with no shared ancestor, so cascading
        // from both into this join table is not the "multiple cascade paths" case SQL Server
        // rejects (that only applies to two paths converging from a single common ancestor).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
