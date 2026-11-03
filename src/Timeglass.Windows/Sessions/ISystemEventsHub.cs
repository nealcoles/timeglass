using Microsoft.Win32;

namespace Timeglass.Windows.Sessions;

/// <summary>
/// The <see cref="SystemEvents"/> events that <see cref="Win32SessionEventSource"/> listens to, behind
/// an interface so tests can raise them.
/// </summary>
internal interface ISystemEventsHub
{
    event SessionSwitchEventHandler? SessionSwitch;

    event PowerModeChangedEventHandler? PowerModeChanged;

    event SessionEndingEventHandler? SessionEnding;
}

/// <summary>Forwards to the static <see cref="SystemEvents"/>.</summary>
internal sealed class SystemEventsHub : ISystemEventsHub
{
    public static readonly SystemEventsHub Instance = new();

    private SystemEventsHub()
    {
    }

    public event SessionSwitchEventHandler? SessionSwitch
    {
        add => SystemEvents.SessionSwitch += value;
        remove => SystemEvents.SessionSwitch -= value;
    }

    public event PowerModeChangedEventHandler? PowerModeChanged
    {
        add => SystemEvents.PowerModeChanged += value;
        remove => SystemEvents.PowerModeChanged -= value;
    }

    public event SessionEndingEventHandler? SessionEnding
    {
        add => SystemEvents.SessionEnding += value;
        remove => SystemEvents.SessionEnding -= value;
    }
}
