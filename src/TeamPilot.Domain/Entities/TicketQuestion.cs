using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A record of why a ticket was blocked mid-pipeline - an agent stage's clarifying question
/// (<see cref="TicketQuestionKind.Question"/>), a stage's proceed-or-cancel decision point
/// (<see cref="TicketQuestionKind.Decision"/> - e.g. a conflicting related ticket), or a known
/// operational failure (<see cref="TicketQuestionKind.Failure"/>, e.g. a Git or LLM call
/// failure). An agent can never cancel a ticket itself, so a <see cref="TicketQuestionKind.Decision"/>
/// is still resolved the same way as a <see cref="TicketQuestionKind.Question"/> - by answering
/// it to resume the pipeline - with an actual cancellation left to the human via the ticket's own
/// Cancel action. References its
/// <see cref="Ticket"/> and <see cref="Agent"/> by id only, matching how <see cref="StageExecution"/>
/// relates to <see cref="Ticket"/> - not eagerly loaded onto <see cref="Ticket"/>, since a
/// ticket can accumulate an unbounded number of these over its lifetime. See docs/domain.md.
/// </summary>
public class TicketQuestion : Entity
{
    public Guid TicketId { get; private set; }

    /// <summary>
    /// The stage's agent that raised this - null when not tied to a specific stage (e.g. a
    /// failure linking the ticket's branch before any stage has run).
    /// </summary>
    public Guid? AgentId { get; private set; }

    public TicketQuestionKind Kind { get; private set; }

    /// <summary>The clarifying question text, or the failure's message.</summary>
    public string Prompt { get; private set; } = string.Empty;

    public TicketQuestionStatus Status { get; private set; }

    public string? AnswerText { get; private set; }

    public string? AnsweredBy { get; private set; }

    public DateTime? AnsweredAtUtc { get; private set; }

    /// <summary>
    /// Set once this question's answer has actually been threaded into a resumed pipeline run's
    /// prompt - prevents an old answer from being re-threaded into a later, unrelated run, since
    /// (unlike a review) a question has no newer record that naturally supersedes it.
    /// </summary>
    public bool Consumed { get; private set; }

    private TicketQuestion()
    {
    }

    public static TicketQuestion CreateQuestion(Guid ticketId, Guid? agentId, string questionText)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            throw new ArgumentException("Question text is required.", nameof(questionText));
        }

        return Create(ticketId, agentId, TicketQuestionKind.Question, questionText);
    }

    public static TicketQuestion CreateFailure(Guid ticketId, Guid? agentId, string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage))
        {
            throw new ArgumentException("Failure message is required.", nameof(failureMessage));
        }

        return Create(ticketId, agentId, TicketQuestionKind.Failure, failureMessage);
    }

    /// <summary>
    /// Raised when a stage's response indicates continuing depends on whether the ticket should
    /// proceed or be cancelled (e.g. it conflicts with another in-flight ticket) - distinct from
    /// <see cref="TicketQuestionKind.Question"/> only so the ticket detail page can point the
    /// human at the ticket's own Cancel action instead of a free-text reply, since the agent
    /// raising this has no ability to cancel the ticket itself.
    /// </summary>
    public static TicketQuestion CreateDecision(Guid ticketId, Guid? agentId, string questionText)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            throw new ArgumentException("Question text is required.", nameof(questionText));
        }

        return Create(ticketId, agentId, TicketQuestionKind.Decision, questionText);
    }

    private static TicketQuestion Create(Guid ticketId, Guid? agentId, TicketQuestionKind kind, string prompt)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        return new TicketQuestion
        {
            TicketId = ticketId,
            AgentId = agentId,
            Kind = kind,
            Prompt = prompt,
            Status = TicketQuestionStatus.Pending,
        };
    }

    /// <summary>
    /// Records a human's answer to a <see cref="TicketQuestionKind.Question"/> or
    /// <see cref="TicketQuestionKind.Decision"/> - a <see cref="TicketQuestionKind.Failure"/> has
    /// nothing to "answer"; it's resolved by retrying the pipeline instead (see
    /// <c>TicketQuestionService.RetryAsync</c>).
    /// </summary>
    public void Answer(string answer, string answeredBy)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new ArgumentException("Answer is required.", nameof(answer));
        }

        if (string.IsNullOrWhiteSpace(answeredBy))
        {
            throw new ArgumentException("Answered by is required.", nameof(answeredBy));
        }

        if (Kind == TicketQuestionKind.Failure)
        {
            throw new InvalidOperationException("Only a clarifying question or decision can be answered - a failure is resolved by retrying the pipeline.");
        }

        if (Status == TicketQuestionStatus.Answered)
        {
            throw new InvalidTicketQuestionStateException(Status, "answer this question");
        }

        AnswerText = answer.Trim();
        AnsweredBy = answeredBy.Trim();
        AnsweredAtUtc = DateTime.UtcNow;
        Status = TicketQuestionStatus.Answered;
        MarkUpdated();
    }

    public void MarkConsumed()
    {
        Consumed = true;
        MarkUpdated();
    }
}
