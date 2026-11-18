using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

internal static class MarkdownPreview
{
    private static readonly Regex BoldMarkup = new Regex(
        @"(?<!\\)\*\*(?=\S)((?:[^*]|\*(?!\*))+?)(?<=\S)\*\*", RegexOptions.CultureInvariant);

    internal static RichTextBox Create(string text, string label)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left };
        foreach (var section in MarkdownTables.Read(text))
        {
            if (section.Rows != null)
                document.Blocks.Add(CreateTable(section));
            else if (section.CodeLines != null)
                document.Blocks.Add(CreateCodeBlock(section.CodeLines));
            else
                document.Blocks.Add(CreateParagraph(section.Lines));
        }
        if (document.Blocks.LastBlock != null)
            document.Blocks.LastBlock.Margin = new Thickness(0);
        var viewer = new RichTextBox(document)
        {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontSize = 14
        };
        viewer.SetResourceReference(Control.FontFamilyProperty, "AppFont");
        viewer.SetResourceReference(Control.ForegroundProperty, "Ink");
        viewer.SetResourceReference(RichTextBox.SelectionBrushProperty, "FocusBrush");
        AutomationProperties.SetName(viewer, label);
        return viewer;
    }

    private static Paragraph CreateCodeBlock(string[] lines)
    {
        var code = new Paragraph
        {
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 13,
            LineHeight = 20,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 9),
            BorderThickness = new Thickness(1),
            KeepTogether = false
        };
        code.SetResourceReference(TextElement.BackgroundProperty, "PanelBrush");
        code.SetResourceReference(Block.BorderBrushProperty, "BorderBrush");
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0)
                code.Inlines.Add(new LineBreak());
            code.Inlines.Add(new Run(lines[index]));
        }
        return code;
    }

    internal static void BindToggle(Button button, TextBox input, ScrollViewer preview, FrameworkElement hint = null)
    {
        button.Click += delegate
        {
            bool showPreview = preview.Visibility != Visibility.Visible;
            preview.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
            input.Visibility = showPreview ? Visibility.Collapsed : Visibility.Visible;
            preview.Content = showPreview ? Create(input.Text, "排版预览") : null;
            button.Content = showPreview ? "继续编辑" : "预览";
            if (hint != null)
                hint.Visibility = !showPreview && input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!showPreview)
                Keyboard.Focus(input);
        };
    }

    private static Paragraph CreateParagraph(string[] lines)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8), LineHeight = 21 };
        int count = lines.Length;
        while (count > 0 && String.IsNullOrWhiteSpace(lines[count - 1]))
            count--;
        int start = 0;
        while (start < count && String.IsNullOrWhiteSpace(lines[start]))
            start++;
        for (int index = start; index < count; index++)
        {
            if (index > start)
                paragraph.Inlines.Add(new LineBreak());
            AddInlines(paragraph, lines[index]);
        }
        return paragraph;
    }

    private static Table CreateTable(MarkdownTables.Section section)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 9) };
        for (int column = 0; column < section.AlignmentMarkers.Length; column++)
        {
            int index = column;
            int length = section.Rows.Max(row => (row[index] ?? "").Length);
            double weight = Math.Sqrt(Math.Max(9, Math.Min(160, length)));
            table.Columns.Add(new TableColumn { Width = new GridLength(weight, GridUnitType.Star) });
        }
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (int rowIndex = 0; rowIndex < section.Rows.Count; rowIndex++)
        {
            var row = new TableRow();
            group.Rows.Add(row);
            for (int column = 0; column < table.Columns.Count; column++)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 21 };
                string marker = section.AlignmentMarkers[column];
                if (marker.EndsWith(":", StringComparison.Ordinal))
                    paragraph.TextAlignment = marker.StartsWith(":", StringComparison.Ordinal) ? TextAlignment.Center : TextAlignment.Right;
                if (rowIndex == 0)
                    paragraph.FontWeight = FontWeights.SemiBold;
                AddInlines(paragraph, section.Rows[rowIndex][column] ?? "");
                var cell = new TableCell(paragraph)
                {
                    Padding = new Thickness(8, 5, 8, 5),
                    BorderThickness = new Thickness(column == 0 ? 1 : 0, rowIndex == 0 ? 1 : 0, 1, 1)
                };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderBrush");
                cell.SetResourceReference(TextElement.BackgroundProperty, rowIndex == 0 ? "SelectedBrush" :
                    (rowIndex % 2 == 0 ? "PanelBrush" : "SurfaceBrush"));
                row.Cells.Add(cell);
            }
        }
        return table;
    }

    private static void AddInlines(Paragraph paragraph, string line)
    {
        int offset = 0;
        foreach (Match match in BoldMarkup.Matches(line))
        {
            if (match.Index > offset)
                paragraph.Inlines.Add(new Run(line.Substring(offset, match.Index - offset)));
            paragraph.Inlines.Add(new Bold(new Run(match.Groups[1].Value)));
            offset = match.Index + match.Length;
        }
        if (offset < line.Length)
            paragraph.Inlines.Add(new Run(line.Substring(offset)));
    }
}
