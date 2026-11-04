using System.Text.Json;
using System.Text.Json.Serialization;
using Timeglass.Core.Settings;

namespace Timeglass.Storage.Settings;

/// <summary>
/// The user's <c>settings.json</c>. The host reads it through the configuration system (which
/// watches it for changes); this class writes it.
/// </summary>
/// <remarks>
/// Writes are atomic: the JSON goes to a temp file in the same folder, which then replaces the
/// settings file in one rename. A crash or power cut mid-write leaves the old file intact, and
/// the file watcher never sees half a file.
/// </remarks>
public static class SettingsFile
{
    /// <summary>File name of the settings file.</summary>
    public const string FileName = "settings.json";

    /// <summary><c>%LOCALAPPDATA%\Timeglass\settings.json</c>, next to the database.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Timeglass",
        FileName);

    /// <summary>The JSON written for <paramref name="settings"/>.</summary>
    public static string Serialize(TimeglassSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, SettingsJsonContext.Default.TimeglassSettings);
    }

    /// <summary>Writes <paramref name="settings"/> to <paramref name="path"/> atomically, creating the folder if needed.</summary>
    public static void Save(string path, TimeglassSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);

        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(folder);

        var json = Serialize(settings);
        var temp = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Writes the default settings if <paramref name="path"/> does not exist yet, so there is a file to edit.</summary>
    /// <returns>True if the file was created.</returns>
    public static bool EnsureExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            return false;
        }

        Save(path, new TimeglassSettings());
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a stray temp file behind is harmless.
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TimeglassSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
