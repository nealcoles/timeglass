namespace Timeglass.Core.Apps;

/// <summary>
/// One app as a person thinks of it, and the executables that implement it.
/// </summary>
/// <param name="Key">Stable lowercase key (<c>a-z</c>, <c>0-9</c>, <c>-</c>), e.g. <c>outlook</c>.</param>
/// <param name="Name">Display name, e.g. <c>Outlook</c>.</param>
/// <param name="Exes">Executable file names, matched case-insensitively, e.g. <c>olk.exe</c>.</param>
/// <param name="TitlePattern">
/// Optional regex on the window title. When set, the alias only applies to windows of these
/// executables whose title matches, and it wins over an alias without a pattern.
/// </param>
public sealed record AppAlias(
    string Key,
    string Name,
    IReadOnlyList<string> Exes,
    string? TitlePattern = null);
