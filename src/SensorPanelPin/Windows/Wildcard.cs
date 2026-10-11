using System.Text;
using System.Text.RegularExpressions;

namespace SensorPanelPin.Windows;

/// <summary>Case-insensitive * / ? matching. A pattern with no wildcards must match in full.</summary>
public static class Wildcard
{
    /// <summary>
    /// Translated patterns have no nested quantifiers, so there is no exponential blow-up, but a
    /// pattern with several literals between stars is quadratic on long input - and window titles
    /// are set by whatever program owns the window. A bound keeps a hostile title from stalling the
    /// UI thread.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

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
                // Singleline: '*' must also cross line breaks, which titles can contain.
                cached = new Regex(
                    Translate(pattern),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
                    MatchTimeout);

                if (Cache.Count > 256)
                {
                    Cache.Clear();
                }

                Cache[pattern] = cached;
            }

            regex = cached;
        }

        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            // Only reachable with a pathological title. Treating it as "no match" means the panel
            // rule does not claim the window, and a user exception may not protect it - a window
            // that could at worst be moved, which the ping-pong guard bounds.
            return false;
        }
    }

    private static string Translate(string pattern)
    {
        // \A and \z, not ^ and $: '$' also matches before a trailing newline, so "abc" matched "abc\n".
        var sb = new StringBuilder(@"\A");
        var previousWasStar = false;
        foreach (var c in pattern)
        {
            if (c == '*')
            {
                // Consecutive stars mean the same thing and each one adds backtracking.
                if (!previousWasStar)
                {
                    sb.Append(".*");
                }

                previousWasStar = true;
                continue;
            }

            previousWasStar = false;
            sb.Append(c == '?' ? "." : Regex.Escape(c.ToString()));
        }

        return sb.Append(@"\z").ToString();
    }
}
