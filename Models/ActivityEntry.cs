namespace FikaHeadlessManager.Models;

/// <summary>
/// Represents one recent manager lifecycle event displayed by the dashboard.
/// </summary>
public sealed record ActivityEntry
{
    /// <summary>Gets the event timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the manager display name.</summary>
    public required string ManagerName { get; init; }

    /// <summary>Gets the event description.</summary>
    public required string Event { get; init; }

    /// <summary>Gets the event severity.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the formatted local event time.</summary>
    public string DisplayTime => Timestamp.LocalDateTime.ToString("MM-dd HH:mm:ss");
}
