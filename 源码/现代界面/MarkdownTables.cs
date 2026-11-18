using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static class MarkdownTables
{
    private static readonly Regex Separator = new Regex("^:?-+:?$", RegexOptions.CultureInvariant);

    internal static IEnumerable<Section> Read(string source)
    {
        string[] lines = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var text = new List<string>();
        for (int index = 0; index < lines.Length;)
        {
            string line = lines[index];
            int indent, fenceLength;
            char marker;
            if (MarkdownCode.IsOpeningFence(line, out indent, out marker, out fenceLength))
            {
                if (text.Count > 0)
                    yield return new Section { Lines = text.ToArray() };
                text.Clear();
                index++;
                var code = new List<string>();
                while (index < lines.Length && !MarkdownCode.IsClosingFence(lines[index], marker, fenceLength))
                {
                    int spaces = 0;
                    string codeLine = lines[index++];
                    while (spaces < indent && spaces < codeLine.Length && codeLine[spaces] == ' ')
                        spaces++;
                    code.Add(codeLine.Substring(spaces));
                }
                if (index < lines.Length)
                    index++;
                yield return new Section { CodeLines = code.ToArray() };
                continue;
            }

            Section table;
            int next;
            if (!TryReadTable(lines, index, out table, out next))
            {
                text.Add(line);
                index++;
                continue;
            }
            if (text.Any(value => value.Length > 0))
                yield return new Section { Lines = text.ToArray() };
            text.Clear();
            yield return table;
            index = next;
        }
        if (text.Count > 0)
            yield return new Section { Lines = text.ToArray() };
    }

    private static bool TryReadTable(string[] lines, int index, out Section table, out int next)
    {
        table = null;
        next = index;
        if (index + 1 >= lines.Length)
            return false;
        string[] header = SplitRow(lines[index]);
        string[] separator = SplitRow(lines[index + 1]);
        if (header == null || separator == null || header.Length != separator.Length ||
            !separator.All(cell => Separator.IsMatch(cell)))
            return false;

        var rows = new List<string[]> { header };
        next = index + 2;
        for (; next < lines.Length; next++)
        {
            string[] row = SplitRow(lines[next]);
            if (row == null || row.Length > header.Length)
                break;
            Array.Resize(ref row, header.Length);
            rows.Add(row);
        }
        table = new Section { Rows = rows, AlignmentMarkers = separator };
        return true;
    }

    private static string[] SplitRow(string line)
    {
        string trimmed = line.Trim();
        var cells = new List<string>();
        var current = new StringBuilder();
        bool hasPipe = false;
        bool leadingPipe = false;
        bool trailingPipe = false;
        for (int index = 0; index < trimmed.Length; index++)
        {
            char ch = trimmed[index];
            trailingPipe = false;
            if (ch == '\\' && index + 1 < trimmed.Length && (trimmed[index + 1] == '|' || trimmed[index + 1] == '\\'))
                current.Append(trimmed[++index]);
            else if (ch == '|')
            {
                leadingPipe |= index == 0;
                trailingPipe = true;
                hasPipe = true;
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
                current.Append(ch);
        }
        if (!hasPipe || trimmed == "|")
            return null;
        if (!trailingPipe)
            cells.Add(current.ToString().Trim());
        if (leadingPipe)
            cells.RemoveAt(0);
        return cells.ToArray();
    }

    internal sealed class Section
    {
        internal string[] Lines { get; set; }
        internal string[] CodeLines { get; set; }
        internal List<string[]> Rows { get; set; }
        internal string[] AlignmentMarkers { get; set; }
    }
}
