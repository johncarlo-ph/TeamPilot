namespace TeamPilot.Application.LiveAgentChat.Dtos;

/// <summary>
/// Starts a new chat session. <see cref="Title"/> is optional - a blank/omitted title falls back
/// to <see cref="TeamPilot.Domain.Entities.Conversation.DefaultTitle"/>, and the user can rename
/// it later via <see cref="RenameConversationRequest"/>.
/// </summary>
public sealed record CreateConversationRequest(string? Title);
