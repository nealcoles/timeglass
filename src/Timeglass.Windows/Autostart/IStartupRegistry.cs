using Microsoft.Win32;

namespace Timeglass.Windows.Autostart;

/// <summary>
/// The registry values that decide what starts when the user signs in, behind an interface so the
/// autostart logic can be tested without touching the registry.
/// </summary>
/// <remarks>
/// A program is in the <c>Run</c> key. Task Manager's <i>Startup apps</i> and Settings do not
/// remove it when the user turns it off; they write a flag under <c>StartupApproved\Run</c>
/// with the same value name instead.
/// </remarks>
public interface IStartupRegistry
{
    /// <summary>The command line of a <c>Run</c> value, or <see langword="null"/> if there is none.</summary>
    string? GetRunValue(string name);

    /// <summary>Creates or overwrites a <c>Run</c> value.</summary>
    void SetRunValue(string name, string command);

    /// <summary>Removes a <c>Run</c> value. Does nothing if it doesn't exist.</summary>
    void DeleteRunValue(string name);

    /// <summary>
    /// The <c>StartupApproved\Run</c> flag for a value, or <see langword="null"/> if Windows has
    /// none (which means enabled).
    /// </summary>
    byte[]? GetApprovedValue(string name);

    /// <summary>Removes a <c>StartupApproved\Run</c> flag. Does nothing if it doesn't exist.</summary>
    void DeleteApprovedValue(string name);
}

/// <summary>The current user's startup values under <c>HKEY_CURRENT_USER</c>. Writable without admin rights.</summary>
public sealed class CurrentUserStartupRegistry : IStartupRegistry
{
    /// <summary>Programs Windows starts when the user signs in.</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Which of the <see cref="RunKey"/> programs the user turned off in Task Manager or Settings.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static readonly CurrentUserStartupRegistry Instance = new();

    private CurrentUserStartupRegistry()
    {
    }

    /// <inheritdoc />
    public string? GetRunValue(string name) => Read(RunKey, name) as string;

    /// <inheritdoc />
    public void SetRunValue(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteRunValue(string name) => Delete(RunKey, name);

    /// <inheritdoc />
    public byte[]? GetApprovedValue(string name) => Read(ApprovedKey, name) as byte[];

    /// <inheritdoc />
    public void DeleteApprovedValue(string name) => Delete(ApprovedKey, name);

    private static object? Read(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValue(name);
    }

    private static void Delete(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
