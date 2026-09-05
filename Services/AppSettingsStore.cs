using FikaHeadlessManager.Models;
using System.IO;
using System.Text.Json;

namespace FikaHeadlessManager.Services;

/// <summary>
/// Persists application preferences in the current user's local application data directory.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettingsStore"/> class.
    /// </summary>
    public AppSettingsStore()
    {
        _settingsPath = AppDataPath.ForFile("settings.json");
    }

    /// <summary>Loads persisted application settings.</summary>
    /// <returns>The persisted settings, or <see langword="null"/> when no settings exist.</returns>
    public async Task<AppSettings?> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions);
    }

    /// <summary>Saves application settings atomically.</summary>
    /// <param name="settings">The settings to persist.</param>
    public async Task SaveAsync(AppSettings settings)
    {
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }
}
