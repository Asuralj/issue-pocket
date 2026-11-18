using System;
using System.Linq;
using System.Text.RegularExpressions;

internal static class MarkdownCode
{
    private const char Backtick = '`';
    private static readonly Regex Language = new Regex(@"^[A-Za-z0-9_+#.-]{1,32}$", RegexOptions.CultureInvariant);

    internal static int FenceLength(string line, out int indent, out char marker)
    {
        indent = 0;
        marker = '\0';
        while (indent < line.Length && line[indent] == ' ')
            indent++;
        if (indent > 3 || indent == line.Length)
            return 0;
        marker = line[indent];
        if (marker != Backtick && marker != '~')
            return 0;
        int end = indent;
        while (end < line.Length && line[end] == marker)
            end++;
        return end - indent;
    }

    internal static bool IsOpeningFence(string line, out int indent, out char marker, out int length)
    {
        length = FenceLength(line, out indent, out marker);
        return length >= 3 && (marker != Backtick || line.IndexOf(Backtick, indent + length) < 0);
    }

    internal static bool IsClosingFence(string line, char marker, int length)
    {
        int indent;
        char found;
        int count = FenceLength(line, out indent, out found);
        return found == marker && count >= length && String.IsNullOrWhiteSpace(line.Substring(indent + count));
    }

    internal static string Wrap(string code, string language)
    {
        int run = 0;
        int longest = 0;
        foreach (char character in code)
        {
            run = character == Backtick ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        // A tilde fence avoids a long delimiter when the code itself contains many backticks.
        char marker = Backtick;
        if (longest >= 3)
        {
            int tildes = code.Split('\n').Select(CountLeadingTildes).DefaultIfEmpty(0).Max();
            if (tildes < longest)
            {
                marker = '~';
                longest = tildes;
            }
        }
        string fence = new string(marker, Math.Max(3, longest + 1));
        return fence + (Language.IsMatch(language ?? "") ? language : "") + "\n" +
            code + (code.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + fence;
    }

    private static int CountLeadingTildes(string line)
    {
        int indent;
        char marker;
        int count = FenceLength(line, out indent, out marker);
        return marker == '~' ? count : 0;
    }
}
