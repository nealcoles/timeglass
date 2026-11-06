using System.Text.Json;

namespace Timeglass.Api;

/// <summary>Where a running app's API is and how to get in: written at startup, deleted on exit.</summary>
/// <param name="Port">The port on 127.0.0.1.</param>
/// <param name="Token">The access token for this run (<see cref="ApiAccessToken.Value"/>).</param>
/// <param name="ProcessId">The app's process, so a file left behind by a crash can be told apart.</param>
public sealed record ApiRuntimeInfo(int Port, string Token, int ProcessId)
{
    /// <summary>The API's base address.</summary>
    public Uri BaseAddress => new($"http://127.0.0.1:{Port}/");
}

/// <summary>
/// The small file that tells other tools (scripts, a future CLI) the API's port and token. It
/// lives in the user's profile, so only that user (and administrators) can read it, which is the
/// same set of people who can read the database.
/// </summary>
public static class ApiRuntimeFile
{
    /// <summary><c>%LOCALAPPDATA%\Timeglass\api.json</c>, next to the database.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Timeglass",
        "api.json");

    /// <summary>Writes <paramref name="info"/> to <paramref name="path"/>, replacing it in one step.</summary>
    public static void Write(string path, ApiRuntimeInfo info)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(info);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(info, JsonSerializerOptions.Web));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The file's contents, or <see langword="null"/> if there is no readable file.</summary>
    public static ApiRuntimeInfo? TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return JsonSerializer.Deserialize<ApiRuntimeInfo>(File.ReadAllText(path), JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Deletes the file if it is there.</summary>
    public static void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
