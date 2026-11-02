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
            return changes;
        }

        var at = Clamp(trackingEvent.At);

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

            case TrackingStopped:
                if (!IsAway)
                {
                    CommitPendingIfDue(at, changes);
                }

                _pending = null;
                CloseCurrent(at, changes);
                IsTracking = false;
                break;
        }

        return changes;
    }

    private void Start(TrackingStarted started, List<IntervalChange> changes)
    {
        DateTimeOffset at;
        if (IsTracking)
        {
            // Restart without stop: close what we have first.
            at = Clamp(started.At);
            _pending = null;
            CloseCurrent(at, changes);
        }
        else
        {
            at = started.At;
            _lastAt = at;
        }

        IsTracking = true;
        _pending = null;
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
            ResumeIfInput(lastInputAt, changes);
        }
        else if (IsIdle(at, lastInputAt))
        {
            GoAway(lastInputAt, changes);
        }
        else
        {
            CommitPendingIfDue(at, changes);
        }

        CheckpointIfDue(at, changes);
    }

    private bool IsIdle(DateTimeOffset at, DateTimeOffset lastInputAt)
    {
        if (Options.IdleThreshold is not { } threshold || at - lastInputAt < threshold)
        {
            return false;
        }

        var app = _pending?.Target.App ?? _current?.App;
        return app is null || !Options.IdleExemptApps.Contains(app.Key);
    }

    private void GoAway(DateTimeOffset lastInputAt, List<IntervalChange> changes)
    {
        // Only switches the user made before walking away count.
        if (_pending is not null && _pending.Since <= lastInputAt)
        {
            CommitPendingIfDue(lastInputAt, changes);
        }

        _pending = null;

        var awayFrom = _current is null || lastInputAt > _current.Start ? lastInputAt : _current.Start;
        CloseCurrent(awayFrom, changes);
        OpenAway(awayFrom, changes);
    }

    private void ResumeIfInput(DateTimeOffset lastInputAt, List<IntervalChange> changes)
    {
        if (_current is null || lastInputAt <= _current.Start)
        {
            return;
        }

        CloseCurrent(lastInputAt, changes);
        OpenActive(lastInputAt, _foreground, changes);
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

        if (_current is not null && _current.Start == pending.Since)
        {
            // The current interval has zero length: replace its content instead of
            // emitting an empty interval.
            _current = _current with
            {
                App = pending.Target.App,
                ExePath = pending.Target.ExePath,
                Title = pending.Target.Title,
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

    private void OpenActive(DateTimeOffset at, WindowSnapshot? window, List<IntervalChange> changes)
    {
        var target = Describe(window);
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

    private void OpenAway(DateTimeOffset at, List<IntervalChange> changes) =>
        Open(new ActivityInterval(
            Id: _newId(at),
            Kind: IntervalKind.Away,
            Start: at,
            End: at,
            IsOpen: true,
            App: null,
            ExePath: null,
            Title: null), changes);

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

    private Target Describe(WindowSnapshot? window) =>
        window is null
            ? new Target(AppIdentity.Unknown, null, null)
            : new Target(_resolver.Resolve(window), window.ExePath, window.Title);

    private sealed record Target(AppIdentity App, string? ExePath, string? Title)
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
