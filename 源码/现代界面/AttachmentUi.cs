using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class AttachmentUi
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".jfif", ".ico", ".webp", ".heic" };
    private static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msp", ".bat", ".cmd", ".scr", ".com", ".cpl", ".hta", ".lnk", ".url",
          ".ps1", ".psm1", ".vbs", ".vbe", ".wsf", ".wsh", ".js", ".jse", ".reg", ".scf", ".pif" };

    internal static Button Card(Window owner, IssueStore store, Attachment attachment, bool compact, Action remove = null)
    {
        string extension = Path.GetExtension(attachment.Name);
        BitmapSource thumbnail = null;
        if (ImageExtensions.Contains(extension))
        {
            try { thumbnail = ReadImage(store, attachment, compact ? 184 : 286); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException ||
                    ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException || ex is FileFormatException))
                    throw;
            }
        }
        bool image = thumbnail != null;
        var card = new Button
        {
            Width = compact ? 131 : 154,
            Height = compact ? 61 : 115,
            Padding = new Thickness(compact ? 7 : 5),
            Margin = new Thickness(0, 0, 8, compact ? 0 : 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ToolTip = attachment.Name + "  ·  " + DisplaySize(attachment.Size) + "\n双击打开，右键另存"
        };
        AutomationProperties.SetName(card, attachment.Name);
        var name = new TextBlock
        {
            Text = attachment.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        var metadata = new TextBlock { Text = DisplaySize(attachment.Size), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        metadata.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(name);
        labels.Children.Add(metadata);

        FrameworkElement preview;
        if (image)
            preview = new Image { Source = thumbnail, Stretch = Stretch.Uniform };
        else
        {
            var badge = new Border { CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 5, 6, 5) };
            badge.SetResourceReference(Border.BackgroundProperty, "SelectedBrush");
            string type = extension.Length > 1 && extension.Length <= 6 ? extension.Substring(1).ToUpperInvariant() : "文件";
            var tag = new TextBlock { Text = type, FontSize = 10, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
            tag.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            badge.Child = tag;
            preview = badge;
        }
        if (compact)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            preview.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(preview);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            card.Content = row;
        }
        else
        {
            var column = new Grid();
            column.RowDefinitions.Add(new RowDefinition());
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var mat = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(4) };
            mat.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            if (!image)
                preview.HorizontalAlignment = HorizontalAlignment.Center;
            mat.Child = preview;
            labels.Margin = new Thickness(4, 4, 4, 0);
            column.Children.Add(mat);
            Grid.SetRow(labels, 1);
            column.Children.Add(labels);
            card.Content = column;
        }
        Action open = delegate { Open(owner, store, attachment, image); };
        var menu = new ContextMenu();
        PocketUi.AddMenu(menu, image ? "查看图片" : "打开副本", open);
        PocketUi.AddMenu(menu, image ? "保存原图…" : "另存为…", delegate { Save(owner, store, attachment); });
        if (remove != null)
        {
            menu.Items.Add(new Separator());
            PocketUi.AddMenu(menu, "从记录移除", remove);
        }
        card.ContextMenu = menu;
        card.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left)
            {
                args.Handled = true;
                open();
            }
        };
        card.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Enter || args.Key == Key.Space)
            {
                args.Handled = true;
                open();
            }
        };
        return card;
    }

    private static BitmapSource ReadImage(IssueStore store, Attachment attachment, int width)
    {
        using (Stream input = store.OpenAttachment(attachment))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = input;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (width > 0)
                image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    private static void Open(Window owner, IssueStore store, Attachment attachment, bool image)
    {
        try
        {
            if (image)
            {
                var viewer = new PictureWindow(ReadImage(store, attachment, 0), attachment.Name,
                    delegate(Window window) { Save(window, store, attachment); });
                viewer.Owner = owner;
                viewer.ShowDialog();
                return;
            }
            if (ExecutableExtensions.Contains(Path.GetExtension(attachment.Name)))
            {
                Save(owner, store, attachment);
                return;
            }
            string folder = Path.Combine(Path.GetTempPath(), PocketUi.AppName, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, attachment.Name);
            store.ExportAttachment(attachment, path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { PocketUi.Error(owner, "无法打开附件。可右键另存后打开。", ex); }
    }

    private static void Save(Window owner, IssueStore store, Attachment attachment)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = attachment.Name, Filter = "所有文件|*.*", Title = "另存附件", AddExtension = false,
            OverwritePrompt = false, RestoreDirectory = true
        };
        if (dialog.ShowDialog(owner) != true)
            return;
        try
        {
            if (File.Exists(dialog.FileName))
                throw new IOException("目标文件已存在，请换一个文件名。");
            store.ExportAttachment(attachment, dialog.FileName);
        }
        catch (Exception ex) { PocketUi.Error(owner, "无法保存附件。", ex); }
    }

    private static string DisplaySize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024L * 1024)
            return (bytes / 1024d).ToString("0.#") + " KB";
        if (bytes < 1024L * 1024 * 1024)
            return (bytes / (1024d * 1024)).ToString("0.#") + " MB";
        return (bytes / (1024d * 1024 * 1024)).ToString("0.#") + " GB";
    }
}
