using Microsoft.Win32;
using Timeglass.Core.Ports;
using Timeglass.Windows.Sessions;

namespace Timeglass.Windows.IntegrationTests.Sessions;

public class SessionEventMapperTests
{
    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, SessionEvent.Locked)]
    [InlineData(SessionSwitchReason.SessionUnlock, SessionEvent.Unlocked)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, SessionEvent.Locked)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, SessionEvent.Locked)]
    [InlineData(SessionSwitchReason.RemoteConnect, SessionEvent.Unlocked)]
    [InlineData(SessionSwitchReason.ConsoleConnect, SessionEvent.Unlocked)]
    public void Session_switches_map_to_lock_and_unlock(SessionSwitchReason reason, SessionEvent expected)
    {
        SessionEventMapper.FromSessionSwitch(reason).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLogon)]
    [InlineData(SessionSwitchReason.SessionLogoff)]
    [InlineData(SessionSwitchReason.SessionRemoteControl)]
    public void Other_session_switches_are_ignored(SessionSwitchReason reason)
    {
        SessionEventMapper.FromSessionSwitch(reason).ShouldBeNull();
    }

    [Theory]
    [InlineData(PowerModes.Suspend, SessionEvent.Suspending)]
    [InlineData(PowerModes.Resume, SessionEvent.Resumed)]
    public void Suspend_and_resume_are_mapped(PowerModes mode, SessionEvent expected)
    {
        SessionEventMapper.FromPowerMode(mode).ShouldBe(expected);
    }

    [Fact]
    public void Power_status_changes_are_ignored()
    {
        SessionEventMapper.FromPowerMode(PowerModes.StatusChange).ShouldBeNull();
    }

    [Fact]
    public void Unknown_values_are_ignored()
    {
        SessionEventMapper.FromSessionSwitch((SessionSwitchReason)999).ShouldBeNull();
        SessionEventMapper.FromPowerMode((PowerModes)999).ShouldBeNull();
    }
}
