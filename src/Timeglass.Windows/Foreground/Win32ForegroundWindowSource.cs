using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Timeglass.Windows.Foreground;

/// <summary>Settings for <see cref="Win32ForegroundWindowSource"/>.</summary>
public sealed record ForegroundWatcherOptions
{
    /// <summary>
    /// Listen to <c>EVENT_SYSTEM_FOREGROUND</c> and <c>EVENT_OBJECT_NAMECHANGE</c>. Off only in tests
    /// of the polling fallback.
    /// </summary>
    public bool UseHooks { get; init; } = true;

    /// <summary>How often the foreground window and title are polled to catch missed events.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Reports the foreground window using WinEvent hooks, with polling as a fallback.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated thread installs out-of-context hooks for foreground changes and for title changes
/// of the foreground window, and runs a message loop, which is where Windows delivers the callbacks.
/// A thread timer on the same loop polls every <see cref="ForegroundWatcherOptions.PollInterval"/>.
/// Everything runs on that one thread, so the change detection needs no locking.
/// </para>
/// <para>
/// <see cref="ForegroundChanged"/> is raised on the watcher thread. Handlers should only queue the
/// snapshot (the tracking service writes it to its channel).
/// </para>
/// </remarks>
public sealed class Win32ForegroundWindowSource : IForegroundWindowSource, IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ForegroundWatcherOptions _options;
    private readonly ILogger _logger;
    private readonly ForegroundChangeDetector _detector = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();

    // Kept in a field so the delegate isn't collected while Windows still calls it.
    private readonly WINEVENTPROC _callback;

    private uint _threadId;
    private Exception? _startError;
    private bool _disposed;

    /// <summary>Starts the watcher thread and returns once the hooks are installed.</summary>
    public Win32ForegroundWindowSource(ForegroundWatcherOptions? options = null, ILogger<Win32ForegroundWindowSource>? logger = null)
    {
        _options = options ?? new ForegroundWatcherOptions();
        _logger = logger ?? NullLogger<Win32ForegroundWindowSource>.Instance;
        _callback = OnWinEvent;

        _thread = new Thread(Run) { IsBackground = true, Name = "Timeglass foreground watcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_started.Wait(StartTimeout))
        {
            throw new TimeoutException("The foreground watcher thread did not start.");
        }

        if (_startError is not null)
        {
            throw new InvalidOperationException("Could not start the foreground watcher.", _startError);
        }
    }

    /// <inheritdoc />
    public event Action<WindowSnapshot?>? ForegroundChanged;

    /// <inheritdoc />
    public WindowSnapshot? GetCurrent() => WindowInspector.Read(PInvoke.GetForegroundWindow());

    /// <summary>Removes the hooks and stops the watcher thread.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0 && PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default))
        {
            if (!_thread.Join(StopTimeout))
            {
                _logger.LogWarning("The foreground watcher thread did not stop within {Timeout}", StopTimeout);
            }
        }

        _started.Dispose();
    }

    private void Run()
    {
        var hooks = new List<UnhookWinEventSafeHandle>();
        nuint timer = 0;
        try
        {
            if (_options.UseHooks)
            {
                hooks.Add(Hook(PInvoke.EVENT_SYSTEM_FOREGROUND));
                hooks.Add(Hook(PInvoke.EVENT_OBJECT_NAMECHANGE));
            }

            timer = PInvoke.SetTimer(HWND.Null, 0, (uint)_options.PollInterval.TotalMilliseconds, null);
            if (timer == 0)
            {
                throw new InvalidOperationException("SetTimer failed.");
            }

            _threadId = PInvoke.GetCurrentThreadId();
        }
#pragma warning disable CA1031 // Reported to the constructor, which throws it on the caller's thread.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _startError = ex;
            Unhook(hooks, timer);
            _started.Set();
            return;
        }

        _started.Set();
        Check(PInvoke.GetForegroundWindow());

        try
        {
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) is { Value: > 0 })
            {
                if (message.message == PInvoke.WM_TIMER && message.hwnd.IsNull)
                {
                    Check(PInvoke.GetForegroundWindow());
                    continue;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        finally
        {
            Unhook(hooks, timer);
        }
    }

    private UnhookWinEventSafeHandle Hook(uint winEvent)
    {
        var hook = PInvoke.SetWinEventHook(winEvent, winEvent, null, _callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        return hook.IsInvalid ? throw new InvalidOperationException($"SetWinEventHook(0x{winEvent:X}) failed.") : hook;
    }

    private static void Unhook(List<UnhookWinEventSafeHandle> hooks, nuint timer)
    {
        foreach (var hook in hooks)
        {
            hook.Dispose();
        }

        if (timer != 0)
        {
            PInvoke.KillTimer(HWND.Null, timer);
        }
    }

    private void OnWinEvent(
        HWINEVENTHOOK hook,
        uint winEvent,
        HWND window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (winEvent == PInvoke.EVENT_OBJECT_NAMECHANGE)
        {
            // Name changes fire for every control everywhere; only the foreground window's own title counts.
            if (objectId != (int)OBJECT_IDENTIFIER.OBJID_WINDOW || childId != (int)PInvoke.CHILDID_SELF
                || window != PInvoke.GetForegroundWindow())
            {
                return;
            }
        }

        Check(window);
    }

    private void Check(HWND window)
    {
        try
        {
            var title = window.IsNull ? null : WindowInspector.ReadTitle(window);
            if (!_detector.IsChange((nint)window, title))
            {
                return;
            }

            ForegroundChanged?.Invoke(WindowInspector.Read(window));
        }
#pragma warning disable CA1031 // An exception here would end the message loop and with it all tracking.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to report a foreground change");
        }
    }
}
