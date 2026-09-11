namespace TeamPilot.Application.Commits.Dtos;

public sealed record CommitDto(
    Guid Id,
    Guid TicketId,
    string BranchName,
    string CommitHash,
    string Message,
    string DiffContent,
    Guid? AuthorAgentId,
    DateTime CreatedAtUtc);
