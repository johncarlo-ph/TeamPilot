using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ConfigurationJson).IsRequired().HasColumnType("nvarchar(max)");

        builder.Navigation(a => a.Instructions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deactivate agents rather than delete them - instruction history must survive.
        builder.HasMany(a => a.Instructions)
            .WithOne()
            .HasForeignKey(i => i.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
