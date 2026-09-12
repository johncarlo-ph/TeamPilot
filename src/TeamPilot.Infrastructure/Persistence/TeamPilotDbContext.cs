using Microsoft.EntityFrameworkCore;
using TeamPilot.Domain.Common;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Infrastructure.Persistence;

public class TeamPilotDbContext(DbContextOptions<TeamPilotDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<Instruction> Instructions => Set<Instruction>();

    public DbSet<InstructionTemplate> InstructionTemplates => Set<InstructionTemplate>();

    public DbSet<Commit> Commits => Set<Commit>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Conflict> Conflicts => Set<Conflict>();

    public DbSet<TicketAgentAssignment> TicketAgentAssignments => Set<TicketAgentAssignment>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserProjectAssignment> UserProjectAssignments => Set<UserProjectAssignment>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TeamPilotDbContext).Assembly);

        // All entity ids are assigned client-side in the domain (Entity.Id = Guid.NewGuid()),
        // never by EF/the database. Without this, EF's default ValueGeneratedOnAdd convention
        // for Guid keys makes it ambiguous whether an already-non-default key means "existing
        // row" or "new row with a pre-assigned id" - entities discovered only via collection
        // fixup (e.g. Ticket.AddCommit adding to the tracked Commits collection, rather than
        // an explicit repository AddAsync) get misclassified as Modified instead of Added,
        // producing a 0-row UPDATE instead of an INSERT.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindProperty(nameof(Entity.Id)) is not null)
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
