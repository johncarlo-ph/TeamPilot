using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class TicketQuestionRepository(TeamPilotDbContext dbContext) : ITicketQuestionRepository
{
    public async Task AddAsync(TicketQuestion question, CancellationToken cancellationToken = default) =>
        await dbContext.TicketQuestions.AddAsync(question, cancellationToken);

    public Task<TicketQuestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.TicketQuestions.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TicketQuestion>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.TicketQuestions
            .AsNoTracking()
            .Where(q => q.TicketId == ticketId)
            .OrderBy(q => q.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<TicketQuestion?> GetMostRecentAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        dbContext.TicketQuestions
            .Where(q => q.TicketId == ticketId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TicketQuestion?> GetMostRecentUnconsumedAnsweredAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        dbContext.TicketQuestions
            .Where(q => q.TicketId == ticketId
                && q.Kind == TicketQuestionKind.Question
                && q.Status == TicketQuestionStatus.Answered
                && !q.Consumed)
            .OrderByDescending(q => q.AnsweredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
