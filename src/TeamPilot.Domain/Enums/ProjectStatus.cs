namespace TeamPilot.Domain.Enums;

/// <summary>
/// Where a <c>Project</c> is in getting its remote repository cloned into its server-managed
/// sandbox. A brand new project starts <see cref="Cloning"/>; the clone runs detached from the
/// creating request (see <c>TeamPilot.Application.Projects.ProjectService.CreateAsync</c>) and
/// flips the project to <see cref="Ready"/> or <see cref="Failed"/> once it finishes.
/// </summary>
public enum ProjectStatus
{
    Cloning,
    Ready,
    Failed
}
