namespace FikaHeadlessManager.Models;

/// <summary>
/// Represents one captured output entry for a managed headless client.
/// </summary>
public sealed record ProcessLogEntry
{
    /// <summary>Gets the manager profile identifier.</summary>
    public required Guid ProfileId { get; init; }

    /// <summary>Gets the manager display name at the time the output was captured.</summary>
    public required string ManagerName { get; init; }

    /// <summary>Gets the output timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the output source, such as manager, stdout, stderr, or BepInEx.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the major output category used for filtering.</summary>
    public required string Category { get; init; }

    /// <summary>Gets the detected output severity level.</summary>
    public required string Level { get; init; }

    /// <summary>Gets the captured output text.</summary>
    public required string Message { get; init; }
}
