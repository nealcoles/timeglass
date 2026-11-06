using System.Globalization;
using System.Text.RegularExpressions;
using Timeglass.Core.Domain;

namespace Timeglass.Core.Tests.Reporting;

/// <summary>Short ways to write intervals and times in reporting tests.</summary>
internal static partial class ReportingData
{
    // Oslo is UTC+1 in winter and UTC+2 in summer. In 2026 the clocks go forward on 29 March
    // and back on 25 October.
    public static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    public static readonly AppIdentity Code = new("code", "Code");
    public static readonly AppIdentity Outlook = new("outlook", "Outlook");
    public static readonly AppIdentity Teams = new("teams", "Teams");

    public static readonly DateTimeOffset Later = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyDictionary<string, AppIdentity> Apps = new Dictionary<string, AppIdentity>
    {
        ["code"] = Code,
        ["outlook"] = Outlook,
        ["teams"] = Teams,
    };

    public static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static ActivityInterval Active(DateTimeOffset start, DateTimeOffset end, AppIdentity? app = null, string? title = null, bool open = false) =>
        new(Guid.NewGuid(), IntervalKind.Active, start, end, open, app ?? Code, null, title);

    public static ActivityInterval Away(DateTimeOffset start, DateTimeOffset end, bool open = false) =>
        new(Guid.NewGuid(), IntervalKind.Away, start, end, open, null, null, null);

    /// <summary>
    /// Parses intervals written as <c>"app start-end [title]"</c> with times in seconds after
    /// <paramref name="origin"/>, e.g. <c>"code 0-60 main.cs"</c>. <c>away</c> is away time, and a
    /// trailing <c>*</c> on the end marks the interval as open.
    /// </summary>
    public static ActivityInterval[] Parse(DateTimeOffset origin, params string[] lines) =>
        [.. lines.Select(line =>
        {
            var parts = line.Split(' ', 3);
            var open = parts[1].EndsWith('*');
            var range = Range().Match(parts[1].TrimEnd('*'));
            var start = origin.AddSeconds(double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture));
            var end = origin.AddSeconds(double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture));
            return parts[0] == "away"
                ? Away(start, end, open)
                : Active(start, end, Apps[parts[0]], parts.Length > 2 ? parts[2] : null, open);
        })];

    [GeneratedRegex(@"^(-?\d+)-(-?\d+)$")]
    private static partial Regex Range();
}
