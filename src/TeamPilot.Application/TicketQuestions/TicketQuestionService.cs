using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.TicketQuestions.Dtos;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.TicketQuestions;

public sealed class TicketQuestionService(
    ITicketRepository ticketRepository,
    ITicketQuestionRepository ticketQuestionRepository,
    IOrchestrationService orchestrationService,
    IProjectAccessGuard projectAccessGuard,
    ICurrentUserContext currentUser,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<AnswerTicketQuestionRequest> answerValidator) : ITicketQuestionService
{
    public async Task<TicketDto> AnswerAsync(Guid ticketId, Guid questionId, AnswerTicketQuestionRequest request, CancellationToken cancellationToken = default)
    {
        await answerValidator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var question = await ticketQuestionRepository.GetByIdAsync(questionId, cancellationToken)
            ?? throw new NotFoundException(nameof(TicketQuestion), questionId);

        if (question.TicketId != ticket.Id)
        {
            throw new NotFoundException(nameof(TicketQuestion), questionId);
        }

        var answeredBy = currentUser.Name ?? currentUser.Email ?? "Unknown";
        question.Answer(request.Answer, answeredBy);
        ticket.Unblock();

        await auditLogger.LogActionAsync(AuditEventType.TicketQuestionAnswered, $"Question answered for ticket '{ticket.Title}' by {answeredBy}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Kicked off detached (see IOrchestrationService.RunPipelineDetached) instead of
        // awaited: a re-run can take minutes (multiple LLM/Git calls per stage), and the status
        // change above already gives the caller everything it needs to show the ticket as In
        // Progress immediately - unlike awaiting inline, a client disconnecting (e.g. a page
        // refresh) can no longer abort the run mid-flight.
        orchestrationService.RunPipelineDetached(ticket.Id);

        return TicketMappings.ToDto(ticket);
    }

    public async Task<TicketDto> RetryAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var question = await ticketQuestionRepository.GetMostRecentAsync(ticket.Id, cancellationToken);
        if (question is null || question.Kind != TicketQuestionKind.Failure)
        {
            throw new InvalidOperationException("Ticket is not blocked by a failure that can be retried - it may be blocked by a clarifying question instead, which needs to be answered.");
        }

        ticket.Unblock();

        await auditLogger.LogActionAsync(AuditEventType.TicketRetried, $"Pipeline retried for ticket '{ticket.Title}' after a blocking failure.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        orchestrationService.RunPipelineDetached(ticket.Id);

        return TicketMappings.ToDto(ticket);
    }
}
