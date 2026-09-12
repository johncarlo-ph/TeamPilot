namespace TeamPilot.Domain.Enums;

public enum ReviewDecision
{
    Approve,
    RequestChanges,

    /// <summary>
    /// Discards the ticket outright: cancels it (see <see cref="Entities.Ticket.Cancel"/>) and,
    /// if it has a linked branch, deletes that branch from the remote - unlike
    /// <see cref="RequestChanges"/>, there is no path back from this. Gated to Admin/Developer
    /// the same way <see cref="Approve"/> is, since both are the ticket's two "final" decisions.
    /// </summary>
    Reject,

    ResolveConflict
}
