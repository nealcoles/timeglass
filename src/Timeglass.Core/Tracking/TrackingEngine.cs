using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

namespace Timeglass.Core.Tracking;

/// <summary>
/// Turns a stream of <see cref="TrackingEvent"/>s into non-overlapping, contiguous intervals.
/// </summary>
/// <remarks>
/// <para>
/// The engine is a pure, single-threaded state machine: it has no clock, no I/O and no locks.
/// Callers feed it events in order and apply the returned changes. This keeps every rule
/// about "what time counts as what" testable with plain unit tests.
/// </para>
/// <para>Window rules:</para>
/// <list type="bullet">
/// <item>A foreground change does not take effect immediately. It becomes <em>pending</em>, and is
/// committed once the new window has stayed in front for <see cref="TrackingOptions.MinimumInterval"/>.
/// The commit is backdated to when the change happened, so no time is lost.</item>
/// <item>If the window changes again before that, the pending window is dropped and the time
/// counts towards the current interval (Alt-Tab flicker).</item>
/// <item>The same app, executable and title as the current interval is not a change.</item>
/// <item>Timestamps that go backwards are clamped to the last seen time, so intervals can never
/// have negative length or overlap.</item>
/// </list>
/// <para>Idle rules:</para>
/// <list type="bullet">
/// <item>On each <see cref="Tick"/>, if there has been no input for <see cref="TrackingOptions.IdleThreshold"/>,
/// the active interval is closed <em>at the last input time</em> (not at detection time) and an
/// <see cref="IntervalKind.Away"/> interval starts there.</item>
/// <item>The away start never goes before the start of the current interval, so earlier,
/// already-closed intervals are never rewritten.</item>
/// <item>A pending switch that happened after the last input is dropped: windows that change
/// without anyone at the keyboard (pop-ups, notifications) don't count.</item>
/// <item>While away, foreground changes are remembered but don't create intervals. When input
/// resumes, the away interval ends at the new last input time and an active interval starts
/// for whatever window is in front.</item>
/// <item>Apps in <see cref="TrackingOptions.IdleExemptApps"/> (video, meetings) never go idle.</item>
/// </list>
/// <para>Session rules:</para>
/// <list type="bullet">
/// <item><see cref="SessionEvent.Locked"/> and <see cref="SessionEvent.Suspending"/> close the active
/// interval at the event time and start <see cref="IntervalKind.Away"/>. If already away (idle), the
/// away interval simply continues.</item>
/// <item>While locked or suspended, input does not end the away interval (typing the password on the
/// lock screen is input too). Only <see cref="SessionEvent.Unlocked"/> / <see cref="SessionEvent.Resumed"/>
/// do, once neither lock nor suspend is left, and tracking resumes for the window in the event.</item>
/// <item>An unlock or resume without a matching lock or suspend (missed event) is treated as a
/// foreground change.</item>
/// <item><see cref="SessionEvent.ShuttingDown"/> closes everything, like <see cref="TrackingStopped"/>.
/// If the shutdown is cancelled, the next foreground change, unlock or resume starts tracking again.</item>
/// <item>A gap between two events longer than <see cref="TrackingOptions.ClockJumpThreshold"/>
/// (sleep with a missed suspend event, or the clock set forward) counts as away from the previous
/// event. Tracking resumes on the first input after the gap, or on unlock/resume.</item>
/// </list>
/// <para>Privacy rules:</para>
/// <list type="bullet">
/// <item>While an app in <see cref="TrackingOptions.ExcludedApps"/> is in front, no interval is open:
/// its time is a gap, as if tracking were paused. Switching to it follows the window rules (it
/// has to stay in front for the minimum interval), and so does switching away from it. The user
/// doesn't go idle in an excluded app, since there is nothing to end. Locking or sleeping still
/// starts <see cref="IntervalKind.Away"/>.</item>
/// <item>Apps in <see cref="TrackingOptions.AppsWithoutTitles"/>, and every app when
/// <see cref="TrackingOptions.RecordWindowTitles"/> is off, get intervals without a title. Title
/// changes within such an app are not window changes.</item>
/// <item>Options can change at any time. On the next event, an open interval of an app that is now
/// excluded is closed (without its title), an open interval whose title may no longer be stored
/// loses it, and if the app in front is no longer excluded, an interval opens for it.</item>
/// </list>
/// <para>
/// Pausing is not an engine state: the host stops the run (<see cref="TrackingStopped"/>), which
/// closes the open interval, and starts a new one (<see cref="TrackingStarted"/>) on resume.
/// Paused time is a gap: nothing is stored for it.
/// </para>
/// </remarks>
public sealed class TrackingEngine
{
    private readonly IAppIdentityResolver _resolver;
    private readonly Func<DateTimeOffset, Guid> _newId;

    private ActivityInterval? _current;
    private Pending? _pending;
    private WindowSnapshot? _foreground;
    private DateTimeOffset _lastAt;
    private DateTimeOffset _lastCheckpoint;
    private DateTimeOffset _resumeAfter;
    private bool _locked;
    private bool _suspended;
    private bool _shutDown;

    /// <summary>Creates an engine.</summary>
    /// <param name="resolver">Maps windows to apps.</param>
    /// <param name="options">Initial options. Can be replaced later via <see cref="Options"/>.</param>
    /// <param name="newId">Id factory; defaults to time-ordered version 7 GUIDs.</param>
    public TrackingEngine(
        IAppIdentityResolver resolver,
        TrackingOptions? options = null,
        Func<DateTimeOffset, Guid>? newId = null)
    {
        _resolver = resolver;
        Options = options ?? TrackingOptions.Default;
        _newId = newId ?? Guid.CreateVersion7;
    }

    /// <summary>Current options. May be replaced at any time; takes effect on the next event.</summary>
    public TrackingOptions Options { get; set; }

    /// <summary>True between <see cref="TrackingStarted"/> and <see cref="TrackingStopped"/>.</summary>
    public bool IsTracking { get; private set; }

    /// <summary>The currently open interval, if tracking.</summary>
    public ActivityInterval? Current => _current;

    private bool IsAway => _current?.Kind == IntervalKind.Away;

    /// <summary>Processes one event and returns the resulting interval changes, in order.</summary>
    public IReadOnlyList<IntervalChange> Handle(TrackingEvent trackingEvent)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);

        var changes = new List<IntervalChange>();

        if (trackingEvent is TrackingStarted started)
        {
            Start(started, changes);
            return changes;
        }

        if (!IsTracking)
        {
            if (_shutDown && RestartsAfterShutdown(trackingEvent, out var window))
            {
                Start(new TrackingStarted(trackingEvent.At, window), changes);
            }

            return changes;
        }

        var previous = _lastAt;
        var at = Clamp(trackingEvent.At);

        var jumped = Options.ClockJumpThreshold is { } jump && at - previous > jump;

        // Before anything else closes the open interval with content the options no longer allow.
        ScrubCurrent(jumped ? previous : at, changes);

        if (jumped)
        {
            GoAway(previous, resumeAfter: at, changes);
        }

        OpenIfNoLongerExcluded(at, changes);

        switch (trackingEvent)
        {
            case ForegroundChanged changed:
                _foreground = changed.Window;
                if (!IsAway)
                {
                    CommitPendingIfDue(at, changes);
                    OnForegroundChanged(at, changed.Window);
                }

                break;

            case Tick tick:
                OnTick(at, tick.LastInputAt, changes);
                break;

            case SessionChanged session:
                OnSessionChanged(at, session, changes);
                break;

            case TrackingStopped:
                Stop(at, changes);
                break;
        }

        return changes;
    }

    private static bool RestartsAfterShutdown(TrackingEvent trackingEvent, out WindowSnapshot? window)
    {
        switch (trackingEvent)
        {
            case ForegroundChanged changed:
                window = changed.Window;
                return true;
            case SessionChanged { Event: SessionEvent.Unlocked or SessionEvent.Resumed } session:
                window = session.Window;
                return true;
            default:
                window = null;
                return false;
        }
    }

    private void Stop(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (!IsAway)
        {
            CommitPendingIfDue(at, changes);
        }

        _pending = null;
        CloseCurrent(at, changes);
        IsTracking = false;
    }

    private void OnSessionChanged(DateTimeOffset at, SessionChanged session, List<IntervalChange> changes)
    {
        switch (session.Event)
        {
            case SessionEvent.Locked or SessionEvent.Suspending:
                _locked |= session.Event == SessionEvent.Locked;
                _suspended |= session.Event == SessionEvent.Suspending;
                if (!IsAway)
                {
                    // A switch made before locking counts; the lock itself ends it.
                    CommitPendingIfDue(at, changes);
                    _pending = null;
                    CloseCurrent(at, changes);
                    OpenAway(at, changes);
                }

                break;

            case SessionEvent.Unlocked or SessionEvent.Resumed:
                var wasSessionAway = _locked || _suspended;
                _locked &= session.Event != SessionEvent.Unlocked;
                _suspended &= session.Event != SessionEvent.Resumed;
                _foreground = session.Window;

                if (IsAway)
                {
                    if (!_locked && !_suspended)
                    {
                        CloseCurrent(at, changes);
                        OpenActive(at, session.Window, changes);
                    }
                }
                else if (!wasSessionAway)
                {
                    // Missed the lock or suspend: just a chance to pick up the window in front.
                    CommitPendingIfDue(at, changes);
                    OnForegroundChanged(at, session.Window);
                }

                break;

            case SessionEvent.ShuttingDown:
                Stop(at, changes);
                _shutDown = true;
                break;
        }
    }

    private void Start(TrackingStarted started, List<IntervalChange> changes)
    {
        DateTimeOffset at;
        if (IsTracking)
        {
            // Restart without stop: close what we have first.
            at = Clamp(started.At);
            ScrubCurrent(at, changes);
            _pending = null;
            CloseCurrent(at, changes);
        }
        else
        {
            // After a pause: never before the end of the previous run, so runs can't overlap.
            at = Clamp(started.At);
        }

        IsTracking = true;
        _pending = null;
        _locked = false;
        _suspended = false;
        _shutDown = false;
        _foreground = started.Window;
        OpenActive(at, started.Window, changes);
    }

    private void OnTick(DateTimeOffset at, DateTimeOffset lastInputAt, List<IntervalChange> changes)
    {
        // Input can't be in the future, whatever the OS reports.
        if (lastInputAt > at)
        {
            lastInputAt = at;
        }

        if (IsAway)
        {
            if (!_locked && !_suspended)
            {
                ResumeIfInput(lastInputAt, changes);
            }
        }
        else if (IsIdle(at, lastInputAt))
        {
            GoAway(lastInputAt, resumeAfter: null, changes);
        }
        else
        {
            CommitPendingIfDue(at, changes);
        }

        CheckpointIfDue(at, changes);
    }

    private bool IsIdle(DateTimeOffset at, DateTimeOffset lastInputAt)
    {
        // In an excluded app nothing is open, so there is nothing to end.
        if (_current is null)
        {
            return false;
        }

        if (Options.IdleThreshold is not { } threshold || at - lastInputAt < threshold)
        {
            return false;
        }

        var app = _pending?.Target.App ?? _current?.App;
        return app is null || !Options.IdleExemptApps.Contains(app.Key);
    }

    /// <summary>
    /// Ends the active interval at <paramref name="since"/> (but not before it started) and opens
    /// an away interval there. Input only ends it once it is later than <paramref name="resumeAfter"/>,
    /// which defaults to the away start. Already away: only moves <paramref name="resumeAfter"/> forward.
    /// </summary>
    private void GoAway(DateTimeOffset since, DateTimeOffset? resumeAfter, List<IntervalChange> changes)
    {
        if (IsAway)
        {
            if (resumeAfter > _resumeAfter)
            {
                _resumeAfter = resumeAfter.Value;
            }

            return;
        }

        // Only switches the user made before walking away count.
        if (_pending is not null && _pending.Since <= since)
        {
            CommitPendingIfDue(since, changes);
        }

        _pending = null;

        var awayFrom = _current is null || since > _current.Start ? since : _current.Start;
        CloseCurrent(awayFrom, changes);
        OpenAway(awayFrom, changes);
        _resumeAfter = resumeAfter > awayFrom ? resumeAfter.Value : awayFrom;
    }

    private void ResumeIfInput(DateTimeOffset lastInputAt, List<IntervalChange> changes)
    {
        if (_current is null || lastInputAt <= _resumeAfter)
        {
            return;
        }

        CloseCurrent(lastInputAt, changes);
        OpenActive(lastInputAt, _foreground, changes);
    }

    /// <summary>
    /// Applies changed privacy options to the open interval: an excluded app's interval is closed
    /// at <paramref name="at"/> without its title, and a title that may no longer be stored is removed.
    /// </summary>
    private void ScrubCurrent(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (_current is not { Kind: IntervalKind.Active, App: { } app } current)
        {
            return;
        }

        if (IsExcluded(app))
        {
            _current = current with { Title = null };
            _pending = null;
            CloseCurrent(at, changes);
        }
        else if (current.Title is not null && !KeepsTitle(app))
        {
            _current = current with { Title = null };
            changes.Add(new IntervalChange(IntervalChangeKind.Updated, _current));
        }
    }

    /// <summary>Opens an interval for the window in front if its app was excluded and no longer is.</summary>
    private void OpenIfNoLongerExcluded(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (_current is null && _pending is null && !Describe(_foreground).Excluded)
        {
            OpenActive(at, _foreground, changes);
        }
    }

    private void OnForegroundChanged(DateTimeOffset at, WindowSnapshot? window)
    {
        var target = Describe(window);

        if (_current is not null && target.Matches(_current))
        {
            // Back to the current window (or a duplicate event): cancel any pending switch.
            _pending = null;
            return;
        }

        if (_current is null && target.Excluded)
        {
            // From one excluded window to another, or back to it before a switch away was due.
            _pending = null;
            return;
        }

        if (_pending is not null && target.Matches(_pending.Target))
        {
            // Duplicate event for the pending window: keep the original change time.
            return;
        }

        // A new candidate. Any previous pending window never made it past the minimum
        // and its time stays with the current interval.
        _pending = new Pending(at, window, target);
    }

    private void CommitPendingIfDue(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (_pending is null || at - _pending.Since < Options.MinimumInterval)
        {
            return;
        }

        var pending = _pending;
        _pending = null;

        // Described again: the options may have changed since the switch.
        var target = Describe(pending.Window);
        if (_current is not null && _current.Start == pending.Since && !target.Excluded)
        {
            // The current interval has zero length: replace its content instead of
            // emitting an empty interval.
            _current = _current with
            {
                App = target.App,
                ExePath = target.ExePath,
                Title = target.Title,
            };
            changes.Add(new IntervalChange(IntervalChangeKind.Updated, _current));
            return;
        }

        CloseCurrent(pending.Since, changes);
        OpenActive(pending.Since, pending.Window, changes);
    }

    private void CheckpointIfDue(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (_current is null || at - _lastCheckpoint < Options.CheckpointInterval)
        {
            return;
        }

        _current = _current with { End = at };
        _lastCheckpoint = at;
        changes.Add(new IntervalChange(IntervalChangeKind.Updated, _current));
    }

    /// <summary>Opens an active interval for <paramref name="window"/>, or nothing if its app is excluded.</summary>
    private void OpenActive(DateTimeOffset at, WindowSnapshot? window, List<IntervalChange> changes)
    {
        var target = Describe(window);
        if (target.Excluded)
        {
            _current = null;
            return;
        }

        Open(new ActivityInterval(
            Id: _newId(at),
            Kind: IntervalKind.Active,
            Start: at,
            End: at,
            IsOpen: true,
            App: target.App,
            ExePath: target.ExePath,
            Title: target.Title), changes);
    }

    private void OpenAway(DateTimeOffset at, List<IntervalChange> changes)
    {
        _resumeAfter = at;
        Open(new ActivityInterval(
            Id: _newId(at),
            Kind: IntervalKind.Away,
            Start: at,
            End: at,
            IsOpen: true,
            App: null,
            ExePath: null,
            Title: null), changes);
    }

    private void Open(ActivityInterval interval, List<IntervalChange> changes)
    {
        _current = interval;
        _lastCheckpoint = interval.Start;
        changes.Add(new IntervalChange(IntervalChangeKind.Opened, interval));
    }

    private void CloseCurrent(DateTimeOffset at, List<IntervalChange> changes)
    {
        if (_current is null)
        {
            return;
        }

        var closed = _current with { End = at, IsOpen = false };
        _current = null;
        changes.Add(new IntervalChange(IntervalChangeKind.Closed, closed));
    }

    private DateTimeOffset Clamp(DateTimeOffset at)
    {
        if (at < _lastAt)
        {
            return _lastAt;
        }

        _lastAt = at;
        return at;
    }

    private Target Describe(WindowSnapshot? window)
    {
        if (window is null)
        {
            return new Target(AppIdentity.Unknown, null, null, IsExcluded(AppIdentity.Unknown));
        }

        var app = _resolver.Resolve(window);
        var excluded = IsExcluded(app);
        return new Target(
            app,
            excluded ? null : window.ExePath,
            excluded || !KeepsTitle(app) ? null : window.Title,
            excluded);
    }

    private bool IsExcluded(AppIdentity app) => Options.ExcludedApps.Contains(app.Key);

    private bool KeepsTitle(AppIdentity app) =>
        Options.RecordWindowTitles && !Options.AppsWithoutTitles.Contains(app.Key);

    /// <summary>What an interval for a window would contain, with the privacy options applied.</summary>
    private sealed record Target(AppIdentity App, string? ExePath, string? Title, bool Excluded)
    {
        public bool Matches(ActivityInterval interval) =>
            interval.Kind == IntervalKind.Active
            && interval.App == App
            && string.Equals(interval.ExePath, ExePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(interval.Title, Title, StringComparison.Ordinal);

        public bool Matches(Target other) =>
            other.App == App
            && string.Equals(other.ExePath, ExePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(other.Title, Title, StringComparison.Ordinal);
    }

    private sealed record Pending(DateTimeOffset Since, WindowSnapshot? Window, Target Target);
}
