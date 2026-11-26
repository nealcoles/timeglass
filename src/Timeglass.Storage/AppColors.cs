using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Timeglass.Storage;

internal static class AppColors
{
    private static readonly Candidate[] Candidates = CreateCandidates();

    public static IReadOnlyDictionary<string, string> Get(
        SqliteConnection connection,
        IReadOnlyCollection<string> keys)
    {
        using var transaction = connection.BeginTransaction();
        var existing = connection.Query<(string Key, string Color)>(
            "SELECT key AS Key, color AS Color FROM app_colors;", transaction: transaction)
            .ToDictionary(row => row.Key, row => row.Color, StringComparer.Ordinal);
        var appKeys = connection.Query<string>("SELECT key FROM apps ORDER BY id;", transaction: transaction);
        var pending = appKeys.Concat(keys.Order(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)
            .Where(key => !existing.ContainsKey(key)).ToArray();

        if (pending.Length == 0)
        {
            transaction.Commit();
            return existing;
        }

        // Greedily maximise the distance to the nearest assigned colour in perceptual OKLab space.
        var existingLabs = existing.Values.Distinct(StringComparer.Ordinal).Select(Parse).ToArray();
        var distances = Candidates.Select(candidate => existingLabs.Length == 0
            ? double.PositiveInfinity
            : existingLabs.Select(lab => Distance(candidate.Lab, lab)).Min()).ToArray();

        foreach (var key in pending)
        {
            var best = 0;
            for (var i = 1; i < distances.Length; i++)
            {
                if (distances[i] > distances[best])
                {
                    best = i;
                }
            }

            var chosen = Candidates[best];
            connection.Execute("INSERT INTO app_colors (key, color) VALUES (@Key, @Color);",
                new { Key = key, Color = chosen.Color }, transaction);
            existing.Add(key, chosen.Color);
            for (var i = 0; i < distances.Length; i++)
            {
                distances[i] = Math.Min(distances[i], Distance(Candidates[i].Lab, chosen.Lab));
            }
        }

        transaction.Commit();
        return existing;
    }

    private static Candidate[] CreateCandidates()
    {
        var candidates = new List<Candidate>();
        foreach (var lightness in new[] { 50, 35, 65 })
        {
            for (var hue = 0; hue < 360; hue++)
            {
                var color = string.Create(CultureInfo.InvariantCulture, $"hsl({hue} 75% {lightness}%)");
                candidates.Add(new Candidate(color, ToLab(hue, lightness)));
            }
        }

        return [.. candidates];
    }

    private static Lab Parse(string color)
    {
        var candidate = Candidates.FirstOrDefault(c => c.Color == color);
        return candidate?.Lab ?? throw new InvalidOperationException($"Unknown stored app colour: {color}");
    }

    private static double Distance(Lab a, Lab b) =>
        Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2);

    private static Lab ToLab(int hue, int lightness)
    {
        var l = lightness / 100.0;
        const double s = 0.75;
        var chroma = (1 - Math.Abs(2 * l - 1)) * s;
        var h = hue / 60.0;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = h switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        var m = l - chroma / 2;
        static double Linear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        r = Linear(r + m);
        g = Linear(g + m);
        b = Linear(b + m);
        var ll = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var mm = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var ss = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return new Lab(
            0.2104542553 * ll + 0.7936177850 * mm - 0.0040720468 * ss,
            1.9779984951 * ll - 2.4285922050 * mm + 0.4505937099 * ss,
            0.0259040371 * ll + 0.7827717662 * mm - 0.8086757660 * ss);
    }

    private sealed record Candidate(string Color, Lab Lab);

    private readonly record struct Lab(double L, double A, double B);
}
