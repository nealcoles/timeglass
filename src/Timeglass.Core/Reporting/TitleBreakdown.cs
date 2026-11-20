using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Where the time in one app went, by window title, over a day or a range of days.</summary>
/// <param name="App">The app. Its latest display name in the range, or the key if it had no time there.</param>
/// <param name="Start">Start of the range (inclusive).</param>
/// <param name="End">End of the range (exclusive).</param>
/// <param name="Total">Active time in the app inside the range.</param>
/// <param name="Titles">Every title, longest first.</param>
/// <remarks>The percentages of <paramref name="Titles"/> add up to exactly 100.</remarks>
public sealed record TitleBreakdown(
    AppIdentity App,
    DateTimeOffset Start,
    DateTimeOffset End,
    TimeSpan Total,
    IReadOnlyList<TitleTotal> Titles);

/// <summary>Time spent in one window title.</summary>
/// <param name="Title">The title, or <see langword="null"/> for time where no title was recorded.</param>
/// <param name="Duration">Active time with this title.</param>
/// <param name="Share">Exact fraction of the app's time, between 0 and 1.</param>
/// <param name="Percent"><paramref name="Share"/> as a whole percentage.</param>
public sealed record TitleTotal(string? Title, TimeSpan Duration, double Share, int Percent);
