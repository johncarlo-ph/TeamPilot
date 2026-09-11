namespace TeamPilot.Domain.Common;

/// <summary>
/// Base class for all domain entities, providing identity and audit timestamps.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; protected set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; protected set; }

    protected void MarkUpdated() => UpdatedAtUtc = DateTime.UtcNow;
}
