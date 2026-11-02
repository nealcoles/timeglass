namespace Timeglass.Core.Ports;

/// <summary>
/// Reports when the user last gave keyboard or mouse input.
/// </summary>
public interface IIdleSource
{
    /// <summary>Time of the last user input.</summary>
    DateTimeOffset GetLastInputTime();
}
