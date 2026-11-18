using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

internal static class FormattedPaste
{
    internal static void Enable(TextBox input)
    {
        DataObject.AddPastingHandler(input, OnPasting);
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs args)
    {
        if (args.IsDragDrop)
            return;
        string text;
        try { text = Read(args.SourceDataObject); }
        catch (ExternalException) { return; }
        if (String.IsNullOrEmpty(text))
            return;
        var input = (TextBox)sender;
        if (input.MaxLength > 0 && (long)input.Text.Length - input.SelectionLength + text.Length > input.MaxLength)
        {
            args.CancelCommand();
            MessageBox.Show(Window.GetWindow(input), "粘贴内容超过此字段的长度限制，请分段粘贴。", "问题随手记",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        args.DataObject = new DataObject(DataFormats.UnicodeText, text);
        args.FormatToApply = DataFormats.UnicodeText;
    }

    internal static string Read(IDataObject data)
    {
        string plain = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : null;
        string html = data.GetDataPresent(DataFormats.Html, false) ? data.GetData(DataFormats.Html, false) as string : null;
        if (plain != null && plain.Length <= 120000)
        {
            string normalized = plain.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var section in MarkdownTables.Read(normalized))
                if (section.CodeLines != null || (section.Rows != null && !String.IsNullOrWhiteSpace(html)))
                    return plain;
            if (LooksLikeIndentedMarkup(normalized))
                return MarkdownCode.Wrap(normalized, null);
        }
        return String.IsNullOrWhiteSpace(html) ? null : ClipboardHtml.ToMarkdown(html);
    }

    private static bool LooksLikeIndentedMarkup(string text)
    {
        string[] lines = text.Split('\n');
        string trimmed = text.Trim();
        if (lines.Length < 3 || !trimmed.StartsWith("<", StringComparison.Ordinal) ||
            !trimmed.EndsWith(">", StringComparison.Ordinal))
            return false;
        foreach (string line in lines)
        {
            if (line.Length > 1 && (line[0] == ' ' || line[0] == '\t') && line.TrimStart().StartsWith("<", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
