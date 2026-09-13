using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.TicketQuestions.Dtos;

public sealed record TicketQuestionDto(
    Guid Id,
    Guid TicketId,
    Guid? AgentId,
    TicketQuestionKind Kind,
    string Prompt,
    TicketQuestionStatus Status,
    string? AnswerText,
    string? AnsweredBy,
    DateTime? AnsweredAtUtc,
    DateTime CreatedAtUtc);
