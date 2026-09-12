using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class InstructionTemplateConfiguration : IEntityTypeConfiguration<InstructionTemplate>
{
    public void Configure(EntityTypeBuilder<InstructionTemplate> builder)
    {
        builder.ToTable("InstructionTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Content).IsRequired().HasColumnType("nvarchar(max)");
    }
}
