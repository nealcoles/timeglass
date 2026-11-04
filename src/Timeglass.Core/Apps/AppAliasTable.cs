using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Timeglass.Core.Apps;

/// <summary>
/// An immutable set of <see cref="AppAlias"/>es with fast lookup by executable name.
/// </summary>
/// <remarks>
/// Lookup order for a window: aliases with a matching <see cref="AppAlias.TitlePattern"/> first
/// (in table order), then the alias that owns the executable. A table never throws on bad
/// content, so a broken user override can't stop tracking; <see cref="Validate"/> reports problems.
/// </remarks>
public sealed partial class AppAliasTable
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Lazy<AppAliasTable> BuiltInTable = new(LoadBuiltIn);

    private readonly Dictionary<string, AppAlias> _byExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(AppAlias Alias, Regex Title)>> _byExeAndTitle =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a table. Later aliases do not replace earlier ones for the same executable.</summary>
    public AppAliasTable(IEnumerable<AppAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        Aliases = [.. aliases];

        foreach (var alias in Aliases)
        {
            var title = alias.TitlePattern is null ? null : TryCreateRegex(alias.TitlePattern);
            if (alias.TitlePattern is not null && title is null)
            {
                continue;
            }

            foreach (var exe in alias.Exes ?? [])
            {
                if (title is null)
                {
                    _byExe.TryAdd(exe, alias);
                }
                else
                {
                    if (!_byExeAndTitle.TryGetValue(exe, out var rules))
                    {
                        _byExeAndTitle[exe] = rules = [];
                    }

                    rules.Add((alias, title));
                }
            }
        }
    }

    /// <summary>The aliases, in lookup order.</summary>
    public IReadOnlyList<AppAlias> Aliases { get; }

    /// <summary>A table with no aliases: every window falls back to its executable.</summary>
    public static AppAliasTable Empty { get; } = new([]);

    /// <summary>The aliases shipped with Timeglass (embedded <c>AppAliases.json</c>).</summary>
    public static AppAliasTable BuiltIn => BuiltInTable.Value;

    /// <summary>Parses the alias JSON format: <c>{ "apps": [ { "key", "name", "exes", "titlePattern"? } ] }</c>.</summary>
    /// <exception cref="JsonException">The JSON does not match the format.</exception>
    public static AppAliasTable Parse(string json)
    {
        var file = JsonSerializer.Deserialize(json, AppAliasJsonContext.Default.AliasFile)
            ?? throw new JsonException("Alias file is empty.");
        return new AppAliasTable(file.Apps);
    }

    /// <summary>
    /// Returns a table with user overrides applied on top of this one.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>An override without a title pattern whose key exists here renames that app and adds
    /// its executables to it, e.g. <c>{ key: "outlook", name: "Mail", exes: [] }</c>.</item>
    /// <item>Any other override is a new app, or a title rule, and is looked up before the existing ones.</item>
    /// <item>Executables claimed by an override without a title pattern are taken away from the
    /// existing apps, so the override always wins. An existing app left without executables is dropped.</item>
    /// </list>
    /// </remarks>
    public AppAliasTable WithOverrides(IEnumerable<AppAlias> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var list = overrides.ToList();
        if (list.Count == 0)
        {
            return this;
        }

        var claimed = list
            .Where(o => o.TitlePattern is null)
            .SelectMany(o => o.Exes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var merged = Aliases
            .Select(a => a with { Exes = [.. (a.Exes ?? []).Where(e => !claimed.Contains(e))] })
            .ToList();
        var added = new List<AppAlias>();

        foreach (var o in list)
        {
            var index = o.TitlePattern is null
                ? merged.FindIndex(a => a.TitlePattern is null && string.Equals(a.Key, o.Key, StringComparison.Ordinal))
                : -1;

            if (index >= 0)
            {
                var existing = merged[index];
                merged[index] = existing with
                {
                    Name = string.IsNullOrWhiteSpace(o.Name) ? existing.Name : o.Name,
                    Exes = [.. existing.Exes.Concat(o.Exes ?? []).Distinct(StringComparer.OrdinalIgnoreCase)],
                };
            }
            else
            {
                added.Add(o);
            }
        }

        return new AppAliasTable(added.Concat(merged.Where(a => a.Exes.Count > 0)));
    }

    /// <summary>Finds the alias for an executable and window title, if any.</summary>
    public AppAlias? Find(string exeName, string? title)
    {
        ArgumentNullException.ThrowIfNull(exeName);

        if (title is not null && _byExeAndTitle.TryGetValue(exeName, out var rules))
        {
            foreach (var (alias, regex) in rules)
            {
                if (IsMatch(regex, title))
                {
                    return alias;
                }
            }
        }

        return _byExe.GetValueOrDefault(exeName);
    }

    /// <summary>Checks the table for problems. Returns an empty list when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var nameByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var plainExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titleRules = new HashSet<(string, string)>();

        foreach (var alias in Aliases)
        {
            var where = $"'{alias.Key}'";

            if (string.IsNullOrEmpty(alias.Key) || !KeyFormat().IsMatch(alias.Key))
            {
                errors.Add($"{where}: key must be lowercase letters, digits and '-'.");
            }

            if (string.IsNullOrWhiteSpace(alias.Name))
            {
                errors.Add($"{where}: name is required.");
            }
            else if (alias.Key is not null && nameByKey.TryGetValue(alias.Key, out var name) && name != alias.Name)
            {
                errors.Add($"{where}: name '{alias.Name}' differs from '{name}' used by the same key.");
            }
            else if (alias.Key is not null)
            {
                nameByKey.TryAdd(alias.Key, alias.Name);
            }

            if (alias.Exes is null || alias.Exes.Count == 0)
            {
                errors.Add($"{where}: needs at least one executable.");
            }

            if (alias.TitlePattern is not null && TryCreateRegex(alias.TitlePattern) is null)
            {
                errors.Add($"{where}: title pattern '{alias.TitlePattern}' is not a valid regex.");
            }

            foreach (var exe in alias.Exes ?? [])
            {
                if (string.IsNullOrWhiteSpace(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || exe.IndexOfAny(['\\', '/']) >= 0)
                {
                    errors.Add($"{where}: '{exe}' is not an executable file name.");
                }
                else if (alias.TitlePattern is null ? !plainExes.Add(exe) : !titleRules.Add((exe.ToLowerInvariant(), alias.TitlePattern)))
                {
                    errors.Add($"{where}: '{exe}' is already used by another app.");
                }
            }
        }

        return errors;
    }

    private static bool IsMatch(Regex regex, string title)
    {
        try
        {
            return regex.IsMatch(title);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex? TryCreateRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AppAliasTable LoadBuiltIn()
    {
        using var stream = typeof(AppAliasTable).Assembly.GetManifestResourceStream("Timeglass.Core.Apps.AppAliases.json")
            ?? throw new InvalidOperationException("Embedded AppAliases.json is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyFormat();
}

internal sealed record AliasFile(IReadOnlyList<AppAlias> Apps);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AliasFile))]
internal sealed partial class AppAliasJsonContext : JsonSerializerContext;
