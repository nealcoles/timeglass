using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Timeglass.Storage.Migrations;

/// <summary>One numbered schema change.</summary>
/// <param name="Version">Schema version after this script; scripts run in ascending order from 1.</param>
/// <param name="Name">Script name, for errors and logs.</param>
/// <param name="Sql">The script. May contain several statements.</param>
public sealed record Migration(int Version, string Name, string Sql);

/// <summary>
/// Applies numbered SQL scripts and tracks the schema version in <c>PRAGMA user_version</c>.
/// </summary>
/// <remarks>
/// Scripts are embedded resources named <c>NNNN_description.sql</c> in this folder. To change the
/// schema, add the next number; never edit a script that has shipped.
/// </remarks>
public static partial class Migrator
{
    private static readonly Lazy<IReadOnlyList<Migration>> EmbeddedMigrations = new(LoadEmbedded);

    /// <summary>The scripts shipped with this build, in order.</summary>
    public static IReadOnlyList<Migration> Embedded => EmbeddedMigrations.Value;

    /// <summary>The schema version this build expects.</summary>
    public static int LatestVersion => Embedded[^1].Version;

    /// <summary>Reads the schema version of an open database. 0 means empty.</summary>
    public static int GetVersion(SqliteConnection connection) =>
        connection.ExecuteScalar<int>("PRAGMA user_version;");

    /// <summary>
    /// Brings the database up to the last of <paramref name="migrations"/> in one transaction:
    /// either every pending script is applied, or none is.
    /// </summary>
    /// <returns>The number of scripts applied; 0 when already up to date.</returns>
    /// <exception cref="InvalidOperationException">
    /// The scripts are not numbered 1..n, or the database is newer than this build.
    /// </exception>
    public static int Apply(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migrations);

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new InvalidOperationException(
                    $"Migration '{migrations[i].Name}' has version {migrations[i].Version}, expected {i + 1}.");
            }
        }

        var current = GetVersion(connection);
        var latest = migrations.Count;
        if (current > latest)
        {
            throw new InvalidOperationException(
                $"The database has schema version {current}, but this version of Timeglass only knows up to {latest}. " +
                "It was probably created by a newer version.");
        }

        if (current == latest)
        {
            return 0;
        }

        using var transaction = connection.BeginTransaction();
        foreach (var migration in migrations.Skip(current))
        {
            connection.Execute(migration.Sql, transaction: transaction);
        }

        connection.Execute(
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {latest};"),
            transaction: transaction);
        transaction.Commit();

        return latest - current;
    }

    private static List<Migration> LoadEmbedded()
    {
        const string Prefix = "Timeglass.Storage.Migrations.";
        var assembly = typeof(Migrator).Assembly;

        var migrations = new List<Migration>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = resource[Prefix.Length..];
            var match = ScriptName().Match(name);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Migration script '{name}' must be named NNNN_description.sql.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            migrations.Add(new Migration(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                name,
                reader.ReadToEnd()));
        }

        migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
        return migrations;
    }

    [GeneratedRegex(@"^(\d{4})_[a-z0-9_]+\.sql$")]
    private static partial Regex ScriptName();
}
