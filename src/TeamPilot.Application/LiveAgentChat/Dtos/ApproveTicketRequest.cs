namespace TeamPilot.Application.LiveAgentChat.Dtos;

/// <summary>
/// The ticket title/description/acceptance criteria to create from a drafted message's proposal.
/// Normally these match the message's <see cref="Domain.Entities.ChatMessage.ProposedTicketTitle"/>/
/// <see cref="Domain.Entities.ChatMessage.ProposedTicketDescription"/>/
/// <see cref="Domain.Entities.ChatMessage.ProposedTicketAcceptanceCriteria"/> verbatim, but the
/// user may edit them in the chat UI before approving, in which case the edited values are used
/// instead and recorded back onto the message.
/// </summary>
public sealed record ApproveTicketRequest(string Title, string? Description, string AcceptanceCriteria);
