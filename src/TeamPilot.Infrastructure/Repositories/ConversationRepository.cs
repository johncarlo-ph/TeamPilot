using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class ConversationRepository(TeamPilotDbContext dbContext) : IConversationRepository
{
    public async Task<IReadOnlyList<Conversation>> ListByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.UpdatedAtUtc ?? c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        await dbContext.Conversations.AddAsync(conversation, cancellationToken);
}
