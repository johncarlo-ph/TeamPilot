namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown for a workflow-structure change that's invalid given the *rest* of the project's
/// stage sequence - e.g. removing a stage another stage still loops back to, reordering past a
/// loop-back target, or a reorder request that doesn't match the project's current stages. Like
/// <see cref="BranchAlreadyLinkedException"/>, this depends on sibling records a single entity
/// can't see on its own, so it's a use-case (Application) concern rather than a
/// <see cref="Domain.Exceptions.DomainException"/>.
/// </summary>
public sealed class InvalidWorkflowOperationException(string message) : Exception(message)
{
}
