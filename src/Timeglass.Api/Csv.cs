using System.Text;

namespace Timeglass.Api;

/// <summary>
/// Writes CSV lines (RFC 4180) that are also safe to open in a spreadsheet.
/// </summary>
/// <remarks>
/// Window titles are text other programs and websites choose, and a spreadsheet runs a cell that
/// starts with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a formula (CSV injection). Such a field
/// gets a leading apostrophe, which spreadsheets show as text. The JSON export has the exact value.
/// </remarks>
internal static class Csv
{
    public const string ContentType = "text/csv; charset=utf-8";

    public const string LineEnd = "\r\n";

    // Tab, carriage return and line feed come before the formula characters in some spreadsheets'
    // parsing; the full-width forms are read as their ASCII twins by others.
    private const string FormulaStarts = "=+-@\t\r\n＝＋－＠";

    /// <summary>Appends <paramref name="fields"/> as one line, with the line end. A null field is empty.</summary>
    public static StringBuilder AppendLine(StringBuilder line, params ReadOnlySpan<string?> fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                line.Append(',');
            }

            AppendField(line, fields[i]);
        }

        return line.Append(LineEnd);
    }

    /// <summary>One line, with the line end.</summary>
    public static string Line(params ReadOnlySpan<string?> fields) => AppendLine(new StringBuilder(), fields).ToString();

    private static void AppendField(StringBuilder line, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var neutralize = FormulaStarts.Contains(value[0], StringComparison.Ordinal);
        var quote = neutralize
            || value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]);
        if (!quote)
        {
            line.Append(value);
            return;
        }

        line.Append('"');
        if (neutralize)
        {
            line.Append('\'');
        }

        line.Append(value).Replace("\"", "\"\"", line.Length - value.Length, value.Length).Append('"');
    }
}
