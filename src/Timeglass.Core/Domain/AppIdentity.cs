namespace Timeglass.Core.Domain;

/// <summary>
/// An application as a person thinks of it, independent of which executable implements it.
/// Classic and new Outlook (<c>outlook.exe</c>, <c>olk.exe</c>) share one identity.
/// </summary>
/// <param name="Key">Stable, lowercase key used for storage and URLs, e.g. <c>outlook</c>.</param>
/// <param name="DisplayName">Name shown to the user, e.g. <c>Outlook</c>.</param>
public sealed record AppIdentity(string Key, string DisplayName)
{
    /// <summary>Used when there is no foreground window or its process could not be identified.</summary>
    public static AppIdentity Unknown { get; } = new("unknown", "Unknown");
}
