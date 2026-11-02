namespace Timeglass.Core.Domain;

/// <summary>
/// What was in the foreground at a point in time, as reported by the OS layer.
/// </summary>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ExePath">Full path of the executable, or an empty string when it could not be read.</param>
/// <param name="ExeName">File name of the executable, e.g. <c>olk.exe</c>.</param>
/// <param name="Title">Window title, or <see langword="null"/> when not available or not recorded.</param>
/// <param name="FileDescription">Executable file description from version info, used as a fallback display name.</param>
public sealed record WindowSnapshot(
    int ProcessId,
    string ExePath,
    string ExeName,
    string? Title,
    string? FileDescription = null);
