namespace Timeglass.Windows.Processes;

/// <summary>What <see cref="ProcessResolver"/> found out about a process.</summary>
/// <param name="ExePath">Full exe path, or empty when it could not be read.</param>
/// <param name="ExeName">Exe file name, <see cref="ProcessResolver.ElevatedExeName"/>, or empty when unknown.</param>
/// <param name="FileDescription">File description from version info, if the exe has one.</param>
internal sealed record ProcessInfo(string ExePath, string ExeName, string? FileDescription)
{
    public static ProcessInfo Unknown { get; } = new(string.Empty, string.Empty, null);

    /// <summary>
    /// A process this user may not query. The description doubles as the display name, so the
    /// app shows up as "Elevated app" under the key <c>unknown-elevated</c>.
    /// </summary>
    public static ProcessInfo Elevated { get; } = new(string.Empty, ProcessResolver.ElevatedExeName, "Elevated app");
}

/// <summary>
/// Resolves a process id to its exe path, exe name and file description.
/// </summary>
/// <remarks>
/// <para>
/// Results are cached by process id and start time, so a window that stays in front is not looked up
/// again on every title change or poll, and a reused process id is not mistaken for the old process.
/// File descriptions are cached per exe path, since many processes share one exe.
/// </para>
/// <para>
/// Processes that this user may not query (elevated or protected) resolve to
/// <see cref="ElevatedExeName"/> instead of failing. Processes that exited resolve to
/// <see cref="ProcessInfo.Unknown"/>. Failures are not cached; they are cheap to repeat.
/// </para>
/// <para>Thread-safe.</para>
/// </remarks>
internal sealed class ProcessResolver
{
    /// <summary>Exe name reported for processes this user may not query.</summary>
    public const string ElevatedExeName = "unknown-elevated";

    /// <summary>Default number of processes and exe paths to remember.</summary>
    public const int DefaultCapacity = 128;

    private readonly IProcessOpener _opener;
    private readonly Func<string, string?> _readDescription;
    private readonly LruCache<(uint ProcessId, long StartTime), ProcessInfo> _processes;
    private readonly LruCache<string, string?> _descriptions;
    private readonly Lock _lock = new();

    /// <param name="opener">Opens processes for querying.</param>
    /// <param name="readDescription">Reads the file description of an exe; may return null.</param>
    /// <param name="capacity">How many processes and exe paths to remember.</param>
    public ProcessResolver(IProcessOpener opener, Func<string, string?> readDescription, int capacity = DefaultCapacity)
    {
        _opener = opener;
        _readDescription = readDescription;
        _processes = new(capacity);
        _descriptions = new(capacity, StringComparer.OrdinalIgnoreCase);
    }

    public ProcessInfo Resolve(uint processId)
    {
        if (processId == 0)
        {
            return ProcessInfo.Unknown;
        }

        var opened = _opener.Open(processId);
        switch (opened.Status)
        {
            case ProcessOpenStatus.AccessDenied:
                return ProcessInfo.Elevated;
            case ProcessOpenStatus.NotFound:
            case ProcessOpenStatus.Opened when opened.Process is null:
                return ProcessInfo.Unknown;
        }

        using var process = opened.Process!;
        var key = (processId, process.StartTime);
        lock (_lock)
        {
            if (_processes.TryGet(key, out var cached))
            {
                return cached;
            }

            var path = process.ReadImagePath();
            if (string.IsNullOrEmpty(path))
            {
                return ProcessInfo.Unknown;
            }

            var info = new ProcessInfo(path, FileName(path), Describe(path));
            _processes.Set(key, info);
            return info;
        }
    }

    private string? Describe(string path)
    {
        if (!_descriptions.TryGet(path, out var description))
        {
            description = _readDescription(path);
            description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            _descriptions.Set(path, description);
        }

        return description;
    }

    /// <summary>File name of a Windows path, also when tests run this on another OS.</summary>
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
