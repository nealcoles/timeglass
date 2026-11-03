using Microsoft.Win32;
using Timeglass.Core.Ports;
using Timeglass.Windows.Sessions;

namespace Timeglass.Windows.IntegrationTests.Sessions;

public class Win32SessionEventSourceTests
{
    private readonly FakeHub _hub = new();
    private readonly List<SessionEvent> _raised = [];

    private Win32SessionEventSource CreateSource()
    {
        var source = new Win32SessionEventSource(_hub);
        source.SessionChanged += _raised.Add;
        return source;
    }

    [Fact]
    public void Lock_and_unlock_are_raised()
    {
        using var source = CreateSource();

        _hub.Switch(SessionSwitchReason.SessionLock);
        _hub.Switch(SessionSwitchReason.SessionUnlock);

        _raised.ShouldBe([SessionEvent.Locked, SessionEvent.Unlocked]);
    }

    [Fact]
    public void Suspend_and_resume_are_raised()
    {
        using var source = CreateSource();

        _hub.Power(PowerModes.Suspend);
        _hub.Power(PowerModes.Resume);

        _raised.ShouldBe([SessionEvent.Suspending, SessionEvent.Resumed]);
    }

    [Theory]
    [InlineData(SessionEndReasons.Logoff)]
    [InlineData(SessionEndReasons.SystemShutdown)]
    public void Logoff_and_shutdown_raise_shutting_down(SessionEndReasons reason)
    {
        using var source = CreateSource();

        _hub.End(reason);

        _raised.ShouldBe([SessionEvent.ShuttingDown]);
    }

    [Fact]
    public void Session_ending_is_not_cancelled()
    {
        using var source = CreateSource();

        var args = _hub.End(SessionEndReasons.SystemShutdown);

        args.Cancel.ShouldBeFalse();
    }

    [Fact]
    public void Ignored_notifications_raise_nothing()
    {
        using var source = CreateSource();

        _hub.Switch(SessionSwitchReason.SessionLogon);
        _hub.Power(PowerModes.StatusChange);

        _raised.ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_unsubscribes_from_every_event()
    {
        var source = CreateSource();
        _hub.HandlerCount.ShouldBe(3);

        source.Dispose();

        _hub.HandlerCount.ShouldBe(0);
        _hub.Switch(SessionSwitchReason.SessionLock);
        _hub.Power(PowerModes.Suspend);
        _hub.End(SessionEndReasons.Logoff);
        _raised.ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        var source = CreateSource();

        source.Dispose();
        source.Dispose();

        _hub.HandlerCount.ShouldBe(0);
    }

    [Fact]
    public void A_failing_handler_does_not_throw_into_system_events()
    {
        using var source = new Win32SessionEventSource(_hub);
        source.SessionChanged += _ => throw new InvalidOperationException("boom");

        Should.NotThrow(() => _hub.Switch(SessionSwitchReason.SessionLock));
    }

    [Fact]
    public void Without_subscribers_nothing_fails()
    {
        using var source = new Win32SessionEventSource(_hub);

        Should.NotThrow(() => _hub.Power(PowerModes.Suspend));
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    [Trait("Category", Desktop.Category)]
    public void Subscribes_to_and_unsubscribes_from_the_real_system_events()
    {
        // Can't lock or suspend the CI machine; this checks that SystemEvents accepts the handlers.
        var source = new Win32SessionEventSource();

        Should.NotThrow(source.Dispose);
    }

    private sealed class FakeHub : ISystemEventsHub
    {
        public event SessionSwitchEventHandler? SessionSwitch;

        public event PowerModeChangedEventHandler? PowerModeChanged;

        public event SessionEndingEventHandler? SessionEnding;

        public int HandlerCount =>
            (SessionSwitch?.GetInvocationList().Length ?? 0)
            + (PowerModeChanged?.GetInvocationList().Length ?? 0)
            + (SessionEnding?.GetInvocationList().Length ?? 0);

        public void Switch(SessionSwitchReason reason) => SessionSwitch?.Invoke(this, new SessionSwitchEventArgs(reason));

        public void Power(PowerModes mode) => PowerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));

        public SessionEndingEventArgs End(SessionEndReasons reason)
        {
            var args = new SessionEndingEventArgs(reason);
            SessionEnding?.Invoke(this, args);
            return args;
        }
    }
}
