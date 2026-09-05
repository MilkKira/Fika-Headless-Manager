using System.IO;

namespace FikaHeadlessManager.Services;

/// <summary>
/// Resolves file paths inside the application's local data folder.
/// </summary>
internal static class AppDataPath
{
    /// <summary>
    /// Returns the absolute path to <paramref name="fileName"/> in the application's
    /// local data directory, creating the directory when it does not exist.
    /// </summary>
    public static string ForFile(string fileName)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FikaHeadlessManager");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
