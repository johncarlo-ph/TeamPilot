using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.RemoteUrl).IsRequired().HasMaxLength(500);
        builder.Property(p => p.EncryptedAccessToken).IsRequired().HasMaxLength(2000);
        builder.Property(p => p.BaseBranch).IsRequired().HasMaxLength(200);
        builder.Property(p => p.RepositoryPath).IsRequired().HasMaxLength(1000);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.CloneFailureReason).HasColumnType("nvarchar(max)");
        builder.Property(p => p.SprintStartDate).HasColumnType("date");
        builder.Property(p => p.SprintEndDate).HasColumnType("date");
        builder.Property(p => p.SprintGoal).HasMaxLength(1000);
    }
}
