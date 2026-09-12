using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class ConversationRepository(TeamPilotDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        await dbContext.Conversations.AddAsync(conversation, cancellationToken);
}
