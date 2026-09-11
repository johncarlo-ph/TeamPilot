using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReviewerName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Comments).HasMaxLength(4000);
    }
}
