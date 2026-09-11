using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence.Configurations;

public class InstructionConfiguration : IEntityTypeConfiguration<Instruction>
{
    public void Configure(EntityTypeBuilder<Instruction> builder)
    {
        builder.ToTable("Instructions");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Content).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(i => i.CreatedBy).HasMaxLength(200);

        builder.HasIndex(i => new { i.AgentId, i.Type, i.Version }).IsUnique();
    }
}
