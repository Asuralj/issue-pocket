using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

internal static class ClipboardHtml
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly Regex WhiteSpace = new Regex(@"[ \t\r\n\f]+", RegexOptions.CultureInvariant);
    private static readonly Regex BoldWeight = new Regex(@"(?:^|;)\s*font-weight\s*:\s*(?:bold|bolder|[6-9]00)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Preformatted = new Regex(@"(?:^|;)\s*white-space\s*:\s*(?:pre|pre-wrap|break-spaces)\s*(?:!important\s*)?(?:;|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StartOffset = new Regex(@"(?im)^StartFragment:[ \t]*(\d+)[ \t]*\r?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex EndOffset = new Regex(@"(?im)^EndFragment:[ \t]*(\d+)[ \t]*\r?$", RegexOptions.CultureInvariant);

    internal static string ToMarkdown(string clipboard)
    {
        if (String.IsNullOrWhiteSpace(clipboard) || clipboard.Length > MaxBytes)
            return null;
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 128 };
        try { document.LoadHtml(GetFragment(clipboard)); }
        catch (Exception error)
        {
            // HtmlAgilityPack throws a plain Exception when the nesting limit is exceeded.
            if (error.GetType() != typeof(Exception) && !(error is ArgumentException || error is InvalidOperationException))
                throw;
            return null;
        }
        var output = new StringBuilder();
        Render(document.DocumentNode, output, false, false);
        return output.ToString().Trim();
    }

    private static string GetFragment(string clipboard)
    {
        int firstTag = clipboard.IndexOf('<');
        string header = firstTag < 0 ? "" : clipboard.Substring(0, firstTag);
        int start, end;
        if (TryOffset(StartOffset, header, out start) && TryOffset(EndOffset, header, out end))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(clipboard);
            if (start < end && end <= bytes.Length)
                return Encoding.UTF8.GetString(bytes, start, end - start);
        }
        const string startMarker = "<!--StartFragment-->";
        start = clipboard.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += startMarker.Length;
            end = clipboard.IndexOf("<!--EndFragment-->", start, StringComparison.OrdinalIgnoreCase);
            if (end >= start)
                return clipboard.Substring(start, end - start);
        }
        return firstTag < 0 ? "" : clipboard.Substring(firstTag);
    }

    private static bool TryOffset(Regex pattern, string header, out int offset)
    {
        var match = pattern.Match(header);
        return Int32.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }

    private static void Render(HtmlNode node, StringBuilder output, bool inCell, bool inBold)
    {
        if (node.NodeType == HtmlNodeType.Comment)
            return;
        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = WhiteSpace.Replace(WebUtility.HtmlDecode(((HtmlTextNode)node).Text), " ");
            if (output.Length > 0 && Char.IsWhiteSpace(output[output.Length - 1]))
                text = text.TrimStart(' ');
            output.Append(text);
            return;
        }

        string tag = node.Name;
        if (!inCell && (tag == "pre" || (tag == "code" && Preformatted.IsMatch(node.GetAttributeValue("style", "")))))
        {
            RenderCode(node, output);
            return;
        }
        switch (tag)
        {
            case "head": case "script": case "style": case "template": case "iframe": case "object":
                return;
            case "table":
                if (!inCell)
                {
                    RenderTable(node, output);
                    return;
                }
                break;
            case "br":
                Break(output, inCell, 1);
                return;
        }

        bool block = tag == "p" || tag == "div" || tag == "section" || tag == "article" ||
            tag == "blockquote" || tag == "pre" || tag == "li" || (tag.Length == 2 && tag[0] == 'h' && Char.IsDigit(tag[1]));
        if (block)
            Break(output, inCell, tag == "li" ? 1 : 2);
        if (tag == "li" && !inCell)
            output.Append("- ");
        bool bold = !inBold && (tag == "strong" || tag == "b" || BoldWeight.IsMatch(node.GetAttributeValue("style", "")));
        if (bold)
        {
            var content = new StringBuilder();
            foreach (var child in node.ChildNodes)
                Render(child, content, inCell, true);
            AppendBold(output, content.ToString());
        }
        else
        {
            foreach (var child in node.ChildNodes)
                Render(child, output, inCell, inBold);
        }
        if (block)
            Break(output, inCell, tag == "li" ? 1 : 2);
    }

    private static void RenderCode(HtmlNode node, StringBuilder output)
    {
        var code = new StringBuilder();
        AppendCodeText(node, code);
        string text = code.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        if (node.Name == "pre" && node.FirstChild != null && node.FirstChild.NodeType == HtmlNodeType.Text &&
            text.StartsWith("\n", StringComparison.Ordinal))
            text = text.Substring(1);
        var codeNode = node.SelectSingleNode("./code") ?? node;
        string language = codeNode.GetAttributeValue("class", "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("language-", StringComparison.OrdinalIgnoreCase));
        Break(output, false, 2);
        output.Append(MarkdownCode.Wrap(text, language == null ? null : language.Substring("language-".Length)));
        Break(output, false, 2);
    }

    private static void AppendCodeText(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Text)
            output.Append(WebUtility.HtmlDecode(((HtmlTextNode)node).Text));
        else if (node.Name == "br")
            output.Append('\n');
        else if (node.Name != "script" && node.Name != "style")
        {
            foreach (var child in node.ChildNodes)
                AppendCodeText(child, output);
        }
    }

    private static void AppendBold(StringBuilder output, string text)
    {
        string core = text.Trim();
        if (core.Length == 0)
            return;
        if (output.Length > 0 && !Char.IsWhiteSpace(output[output.Length - 1]) && Char.IsWhiteSpace(text[0]))
            output.Append(' ');
        output.Append("**").Append(core).Append("**");
        if (Char.IsWhiteSpace(text[text.Length - 1]))
            output.Append(' ');
    }

    private static void RenderTable(HtmlNode table, StringBuilder output)
    {
        var rows = table.SelectNodes("./tr|./thead/tr|./tbody/tr|./tfoot/tr");
        if (rows == null)
            return;
        var cells = rows.Select(row => row.ChildNodes.Where(cell => cell.Name == "td" || cell.Name == "th").ToArray())
            .Where(row => row.Length > 0).ToArray();
        if (cells.Length == 0)
            return;
        int columns = cells.Max(row => row.Length);
        if (columns > 64 || (long)columns * cells.Length > 10000)
        {
            RenderTableAsText(cells, output);
            return;
        }
        Break(output, false, 2);
        for (int row = 0; row < cells.Length; row++)
        {
            output.Append('|');
            for (int column = 0; column < columns; column++)
            {
                string text = column < cells[row].Length ? CellText(cells[row][column]) : "";
                output.Append(' ').Append(text.Replace("\\", "\\\\").Replace("|", "\\|")).Append(" |");
            }
            output.Append('\n');
            if (row == 0)
            {
                output.Append('|');
                for (int column = 0; column < columns; column++)
                    output.Append(" --- |");
                output.Append('\n');
            }
        }
        Break(output, false, 2);
    }

    private static void RenderTableAsText(HtmlNode[][] cells, StringBuilder output)
    {
        Break(output, false, 2);
        foreach (var row in cells)
            output.Append(String.Join(" · ", row.Select(CellText))).Append('\n');
        Break(output, false, 2);
    }

    private static string CellText(HtmlNode cell)
    {
        var output = new StringBuilder();
        Render(cell, output, true, false);
        return output.ToString().Trim();
    }

    private static void Break(StringBuilder output, bool inCell, int lines)
    {
        if (output.Length == 0)
            return;
        if (inCell)
        {
            if (!Char.IsWhiteSpace(output[output.Length - 1]))
                output.Append(' ');
            return;
        }
        while (output.Length > 0 && output[output.Length - 1] == ' ')
            output.Length--;
        if (output.Length == 0)
            return;
        int count = 0;
        while (count < output.Length && output[output.Length - count - 1] == '\n')
            count++;
        if (count < lines)
            output.Append('\n', lines - count);
    }
}
