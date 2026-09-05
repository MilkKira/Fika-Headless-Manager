using FikaHeadlessManager.Models;
using System.IO;
using System.Text.Json;

namespace FikaHeadlessManager.Services;

/// <summary>
/// Persists manager profiles in the current user's local application data directory.
/// </summary>
public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _configurationPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationStore"/> class.
    /// </summary>
    public ConfigurationStore()
    {
        _configurationPath = AppDataPath.ForFile("instances.json");
    }

    /// <summary>Loads all persisted manager profiles.</summary>
    /// <returns>A list of persisted profiles, or an empty list when no settings exist.</returns>
    public async Task<IReadOnlyList<ManagerProfile>> LoadAsync()
    {
        if (!File.Exists(_configurationPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_configurationPath);
        return await JsonSerializer.DeserializeAsync<List<ManagerProfile>>(stream, SerializerOptions)
            ?? [];
    }

    /// <summary>Saves all manager profiles atomically.</summary>
    /// <param name="profiles">The profiles to persist.</param>
    public async Task SaveAsync(IEnumerable<ManagerProfile> profiles)
    {
        var temporaryPath = _configurationPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, profiles, SerializerOptions);
        }

        File.Move(temporaryPath, _configurationPath, true);
    }
}
