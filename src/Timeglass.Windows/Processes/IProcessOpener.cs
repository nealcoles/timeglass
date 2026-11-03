namespace Timeglass.Windows.Processes;

/// <summary>Why a process could or could not be opened.</summary>
internal enum ProcessOpenStatus
{
    /// <summary>The process was opened.</summary>
    Opened,

    /// <summary>
    /// The process exists but this user may not query it (elevated, protected or system processes).
    /// </summary>
    AccessDenied,

    /// <summary>The process has exited or the id was never valid.</summary>
    NotFound,
}

/// <summary>Outcome of <see cref="IProcessOpener.Open"/>; <see cref="Process"/> is set only when opened.</summary>
internal readonly record struct ProcessOpenResult(ProcessOpenStatus Status, IOpenProcess? Process)
{
    public static ProcessOpenResult AccessDenied { get; } = new(ProcessOpenStatus.AccessDenied, null);

    public static ProcessOpenResult NotFound { get; } = new(ProcessOpenStatus.NotFound, null);

    public static ProcessOpenResult Opened(IOpenProcess process) => new(ProcessOpenStatus.Opened, process);
}

/// <summary>
/// Opens processes for querying. The Win32 implementation is <see cref="Win32ProcessOpener"/>;
/// tests use a fake to simulate access denied and process id reuse.
/// </summary>
internal interface IProcessOpener
{
    ProcessOpenResult Open(uint processId);
}

/// <summary>An open process handle.</summary>
internal interface IOpenProcess : IDisposable
{
    /// <summary>
    /// When the process started, as a FILETIME tick count; 0 if unknown. Together with the process id
    /// this identifies a process even after Windows reuses the id.
    /// </summary>
    long StartTime { get; }

    /// <summary>Full path of the executable, or null if it could not be read.</summary>
    string? ReadImagePath();
}
