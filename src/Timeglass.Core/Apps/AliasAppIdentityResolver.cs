using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

namespace Timeglass.Core.Apps;

/// <summary>
/// Resolves windows to apps through an <see cref="AppAliasTable"/>, so that classic and new
/// Outlook (<c>outlook.exe</c>, <c>olk.exe</c>) are one app.
/// </summary>
/// <remarks>
/// Executables without an alias fall back to their own app: the key is the exe name without
/// extension, and the display name is the file description from version info, else the exe name.
/// </remarks>
public sealed class AliasAppIdentityResolver : IAppIdentityResolver
{
    /// <summary>Creates a resolver.</summary>
    /// <param name="table">The aliases; typically <see cref="AppAliasTable.BuiltIn"/> with user overrides.</param>
    public AliasAppIdentityResolver(AppAliasTable table)
    {
        Table = table;
    }

    /// <summary>
    /// The aliases in use. Can be replaced at any time (e.g. when settings change); a reference
    /// swap is atomic and the table itself is immutable.
    /// </summary>
    public AppAliasTable Table { get; set; }

    /// <inheritdoc />
    public AppIdentity Resolve(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var exe = string.IsNullOrWhiteSpace(window.ExeName) ? FileName(window.ExePath) : window.ExeName;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return AppIdentity.Unknown;
        }

        if (Table.Find(exe, window.Title) is { } alias)
        {
            return new AppIdentity(alias.Key, alias.Name);
        }

        var name = Path.GetFileNameWithoutExtension(exe);
        if (string.IsNullOrWhiteSpace(name))
        {
            return AppIdentity.Unknown;
        }

        var display = string.IsNullOrWhiteSpace(window.FileDescription) ? name : window.FileDescription;
        return new AppIdentity(name.ToLowerInvariant(), display);
    }

    /// <summary>
    /// File name of a Windows path. <see cref="Path.GetFileName(string)"/> only splits on the
    /// current OS's separators, and Core also runs (in tests) on Linux.
    /// </summary>
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
