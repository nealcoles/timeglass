using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Timeglass.Core.Ports;

namespace Timeglass.Windows.Sessions;

/// <summary>Reports lock, sleep and shutdown events using <see cref="SystemEvents"/>.</summary>
/// <remarks>
/// <para>
/// Listens to <see cref="SystemEvents.SessionSwitch"/> (lock, unlock, remote connect and disconnect),
/// <see cref="SystemEvents.PowerModeChanged"/> (suspend, resume) and
/// <see cref="SystemEvents.SessionEnding"/> (<c>WM_QUERYENDSESSION</c>: logoff and shutdown). The
/// mapping is in <see cref="SessionEventMapper"/>.
/// </para>
/// <para>
/// <see cref="SessionChanged"/> is raised on the thread that runs the <see cref="SystemEvents"/>
/// window. For <see cref="SessionEvent.ShuttingDown"/>, Windows waits for the handler to return
/// before it continues ending the session, so a handler may block briefly to store its data (the
/// tracking service does, for up to five seconds).
/// </para>
/// <para>
/// Create it on a thread that runs a message loop (the tray app's UI thread) or on an MTA thread,
/// where <see cref="SystemEvents"/> starts its own. Dispose it to unsubscribe:
/// <see cref="SystemEvents"/> is static and keeps every handler, and so this object, alive otherwise.
/// </para>
/// </remarks>
public sealed class Win32SessionEventSource : ISessionEventSource, IDisposable
{
    private readonly ISystemEventsHub _hub;
    private readonly ILogger _logger;
    private int _disposed;

    /// <summary>Subscribes to <see cref="SystemEvents"/>.</summary>
    public Win32SessionEventSource(ILogger<Win32SessionEventSource>? logger = null)
        : this(SystemEventsHub.Instance, logger)
    {
    }

    internal Win32SessionEventSource(ISystemEventsHub hub, ILogger<Win32SessionEventSource>? logger = null)
    {
        _hub = hub;
        _logger = logger ?? NullLogger<Win32SessionEventSource>.Instance;

        _hub.SessionSwitch += OnSessionSwitch;
        _hub.PowerModeChanged += OnPowerModeChanged;
        _hub.SessionEnding += OnSessionEnding;
    }

    /// <inheritdoc />
    public event Action<SessionEvent>? SessionChanged;

    /// <summary>Unsubscribes from <see cref="SystemEvents"/>. No events are raised afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _hub.SessionSwitch -= OnSessionSwitch;
        _hub.PowerModeChanged -= OnPowerModeChanged;
        _hub.SessionEnding -= OnSessionEnding;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        Raise(SessionEventMapper.FromSessionSwitch(e.Reason), e.Reason);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        Raise(SessionEventMapper.FromPowerMode(e.Mode), e.Mode);

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) =>
        Raise(SessionEvent.ShuttingDown, e.Reason);

    private void Raise(SessionEvent? sessionEvent, Enum source)
    {
        if (sessionEvent is not { } value || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _logger.LogDebug("Session event {Event} from {Source}", value, source);

        try
        {
            SessionChanged?.Invoke(value);
        }
#pragma warning disable CA1031 // This runs on the SystemEvents thread; an exception there would end the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "A session event handler failed for {Event}", value);
        }
    }
}
