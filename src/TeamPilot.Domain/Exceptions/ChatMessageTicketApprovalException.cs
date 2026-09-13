namespace TeamPilot.Domain.Exceptions;

public sealed class ChatMessageTicketApprovalException : DomainException
{
    public ChatMessageTicketApprovalException(string message)
        : base(message)
    {
    }
}
