using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).IsRequired().HasMaxLength(200);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // EF Core's primitive-collection support (a JSON array column on SQL Server) - avoids
        // a separate child table for what's just a small fixed set of role enum values.
        builder.PrimitiveCollection(u => u.Roles)
            .HasColumnName("Roles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
