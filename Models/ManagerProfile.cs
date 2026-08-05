using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FikaHeadlessManager.Models;

/// <summary>
/// Describes one managed Fika headless client and exposes its live state to the dashboard.
/// </summary>
public sealed class ManagerProfile : INotifyPropertyChanged
{
    private string _status = "Stopped";
    private int? _processId;
    private DateTimeOffset? _startedAt;

    /// <summary>Occurs when a bindable property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or sets the persistent identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = "New manager";

    /// <summary>Gets or sets the SPT installation directory.</summary>
    public string InstallDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the Fika profile token.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Gets or sets the SPT backend URL.</summary>
    public string BackendUrl { get; set; } = "https://127.0.0.1:6969/";

    /// <summary>Gets or sets the optional game window title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a value that indicates whether extra file logging is enabled.</summary>
    public bool ExtraLogging { get; set; }

    /// <summary>Gets or sets a value that indicates whether an exited client restarts automatically.</summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>Gets the current lifecycle status.</summary>
    [JsonIgnore]
    public string Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    /// <summary>Gets the operating-system process identifier when running.</summary>
    [JsonIgnore]
    public int? ProcessId
    {
        get => _processId;
        internal set => SetField(ref _processId, value);
    }

    /// <summary>Gets the most recent successful start time.</summary>
    [JsonIgnore]
    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        internal set
        {
            if (SetField(ref _startedAt, value))
            {
                OnPropertyChanged(nameof(Uptime));
            }
        }
    }

    /// <summary>Gets a human-readable running duration.</summary>
    [JsonIgnore]
    public string Uptime
    {
        get
        {
            if (StartedAt is null || Status != "Running")
            {
                return "—";
            }

            var elapsed = DateTimeOffset.Now - StartedAt.Value;
            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                : $"{elapsed.Minutes}m {elapsed.Seconds}s";
        }
    }

    /// <summary>Raises a change notification for the calculated uptime.</summary>
    internal void RefreshUptime() => OnPropertyChanged(nameof(Uptime));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
