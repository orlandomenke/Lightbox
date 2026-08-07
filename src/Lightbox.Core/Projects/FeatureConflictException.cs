namespace Lightbox.Core.Projects;

/// <summary>
/// Raised when an export or operation is attempted with conflicting features enabled.
/// The reason explains what is wrong and what needs to be done to proceed.
/// </summary>
public sealed class FeatureConflictException(string reason) : InvalidOperationException(reason)
{
    /// <summary>The human-readable explanation of the conflict and how to resolve it.</summary>
    public string Reason { get; } = reason;
}
