using System.Text.RegularExpressions;

namespace DeskKeeper.Domain.Rules;

public static class Glob
{
    public static bool IsMatch(string input, string pattern, bool ignoreCase = true)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        return Regex.IsMatch(input, regex, options);
    }

    public static bool MatchesAnyExtension(string fileName, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (IsMatch(fileName, pattern))
            {
                return true;
            }
        }

        return false;
    }
}

