namespace TeamPilot.Application.Mentions.Dtos;

public sealed record TicketMentionResultDto(Guid Id, string Title, Guid ProjectId, string ProjectName);
