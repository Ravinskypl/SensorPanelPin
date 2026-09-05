using System.Text;
using System.Text.RegularExpressions;

namespace SensorPanelGlue.Windows;

/// <summary>Case-insensitive * / ? matching. A pattern with no wildcards must match in full.</summary>
public static class Wildcard
{
    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static bool IsMatch(string? value, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        value ??= string.Empty;

        Regex regex;
        lock (Gate)
        {
            if (!Cache.TryGetValue(pattern, out var cached))
            {
                cached = new Regex(Translate(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (Cache.Count > 256)
                {
                    Cache.Clear();
                }

                Cache[pattern] = cached;
            }

            regex = cached;
        }

        return regex.IsMatch(value);
    }

    private static string Translate(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var c in pattern)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return sb.Append('$').ToString();
    }
}
