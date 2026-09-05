namespace FikaHeadlessManager.Models;

/// <summary>
/// Persists user-visible application preferences.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the selected application theme ("Light" or "Dark").</summary>
    public string Theme { get; set; } = "Light";
}
