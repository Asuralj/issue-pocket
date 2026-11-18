using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class QuickWindow : Window
{
    private const int MaxAttachments = 100;
    private readonly IssueStore store;
    private readonly Issue original;
    private readonly Action<Issue> saveIssue;
    private readonly List<Attachment> attachments;
    private readonly Grid page;
    private readonly TextBox title;
    private readonly TextBox body;
    private readonly TextBox result;
    private readonly TextBox category;
    private readonly string initialCategory;
    private readonly ToggleButton pending;
    private readonly ToggleButton resolved;
    private readonly Button save;
    private readonly Button addFiles;
    private readonly TextBlock feedback;
    private readonly Border dropArea;
    private bool busy;
    private bool saved;

    internal QuickWindow(IssueStore store, Issue issue, string selectedCategory, Func<IEnumerable<string>> categories,
                         Func<Window, string> createCategory, Action<Issue> saveIssue, bool focusResult)
    {
        this.store = store;
        original = issue;
        this.saveIssue = saveIssue;
        initialCategory = issue == null ? selectedCategory ?? IssueStore.DefaultCategory : issue.Category;
        attachments = issue == null ? new List<Attachment>() : new List<Attachment>(issue.Attachments);
        PocketUi.Window(this, issue == null ? "记录问题" : "编辑问题", 714, 639);
        MinWidth = 470;
        MinHeight = 580;
        if (focusResult || (issue != null && !String.IsNullOrWhiteSpace(issue.Result)))
            Height = Math.Max(MinHeight, Math.Min(728, SystemParameters.WorkArea.Height - 16));
        page = (Grid)PocketUi.Load("Quick.xaml");
        Content = page;
        Find<TextBlock>("Heading").Text = Title;
        title = Find<TextBox>("TitleInput");
        body = Find<TextBox>("BodyInput");
        category = Find<TextBox>("CategoryInput");
        result = Find<TextBox>("ResultInput");
        pending = Find<ToggleButton>("Pending");
        resolved = Find<ToggleButton>("Resolved");
        save = Find<Button>("Save");
        addFiles = Find<Button>("AddFiles");
        feedback = Find<TextBlock>("Feedback");
        dropArea = Find<Border>("DropArea");
        BindHint(title, "TitleHint", issue == null ? "" : issue.Title);
        BindHint(body, "BodyHint", issue == null ? "" : issue.Body);
        BindHint(result, "ResultHint", issue == null ? "" : issue.Result);
        FormattedPaste.Enable(body);
        FormattedPaste.Enable(result);
        MarkdownPreview.BindToggle(Find<Button>("PreviewBody"), body, Find<ScrollViewer>("BodyPreview"), Find<TextBlock>("BodyHint"));
        MarkdownPreview.BindToggle(Find<Button>("PreviewResult"), result, Find<ScrollViewer>("ResultPreview"), Find<TextBlock>("ResultHint"));
        category.Text = initialCategory;
        SetStatus(issue != null && issue.Resolved);
        pending.Click += delegate { SetStatus(false); };
        resolved.Click += delegate { SetStatus(true); };
        var categoryButton = Find<Button>("CategoryMenu");
        var categoryMenu = new ContextMenu();
        categoryButton.ContextMenu = categoryMenu;
        categoryMenu.Opened += delegate { PopulateCategoryMenu(categoryMenu, categories(), createCategory); };
        categoryButton.Click += delegate { PocketUi.OpenMenu(categoryMenu, categoryButton); };
        Find<Button>("ShowResult").Click += delegate { RevealResult(true); };
        if (focusResult || !String.IsNullOrEmpty(result.Text))
            RevealResult(false);
        save.Click += delegate { Commit(); };
        Find<Button>("Cancel").Click += delegate { Close(); };
        addFiles.Click += delegate { ChooseFiles(); };
        RenderAttachments();
        AllowDrop = true;
        PreviewDragOver += OnDrag;
        PreviewDragEnter += OnDrag;
        PreviewDragLeave += delegate { dropArea.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); };
        PreviewDrop += OnDrop;
        PreviewKeyDown += OnKey;
        Loaded += delegate
        {
            Keyboard.Focus(focusResult ? result : body);
            if (focusResult)
                result.BringIntoView();
        };
    }

    private T Find<T>(string name) where T : FrameworkElement
    {
        return (T)page.FindName(name);
    }

    private void PopulateCategoryMenu(ContextMenu menu, IEnumerable<string> categories, Func<Window, string> createCategory)
    {
        menu.Items.Clear();
        PocketUi.AddMenu(menu, "＋ 新建分类…", delegate
        {
            string name = createCategory(this);
            if (name != null)
                category.Text = name;
        });
        menu.Items.Add(new Separator());
        foreach (string name in categories)
            PocketUi.AddMenu(menu, name, delegate { category.Text = name; });
    }

    internal bool IsEditing(string id)
    {
        return original != null && original.Id == id;
    }

    internal void RevealResult(bool focus)
    {
        Find<StackPanel>("ResultFields").Visibility = Visibility.Visible;
        Find<Button>("ShowResult").Visibility = Visibility.Collapsed;
        if (focus)
        {
            Keyboard.Focus(result);
            result.BringIntoView();
        }
    }

    private void BindHint(TextBox input, string name, string value)
    {
        var hint = Find<TextBlock>(name);
        input.TextChanged += delegate { hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
        input.Text = value ?? "";
        hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(bool isResolved)
    {
        pending.IsChecked = !isResolved;
        resolved.IsChecked = isResolved;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Commit();
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = PasteAttachment();
        }
    }

    private bool PasteAttachment()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null)
                return false;
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null)
                {
                    ImportFiles(paths);
                    return true;
                }
            }
            if ((body.IsKeyboardFocusWithin || result.IsKeyboardFocusWithin) &&
                data.GetDataPresent(DataFormats.Html, false) && data.GetDataPresent(DataFormats.UnicodeText))
                return false;
            var image = ClipboardImage(data);
            if (image == null)
                return false;
            if (busy || !HasRoom())
                return true;
            using (var stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
                stream.Position = 0;
                string name = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                AddToDraft(store.AddAttachment(name, stream));
            }
            RenderAttachments();
            feedback.Text = "已粘贴截图";
            return true;
        }
        catch (Exception ex)
        {
            PocketUi.Error(this, "无法读取剪贴板。", ex);
            return true;
        }
    }

    internal static BitmapSource ClipboardImage(IDataObject data)
    {
        foreach (string format in new[] { "PNG", "image/png" })
        {
            if (!data.GetDataPresent(format, false))
                continue;
            object content = data.GetData(format, false);
            var bytes = content as byte[];
            using (var stream = bytes == null ? content as Stream : new MemoryStream(bytes, false))
            {
                if (stream == null)
                    continue;
                if (stream.CanSeek)
                    stream.Position = 0;
                return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
        }
        return data.GetDataPresent(DataFormats.Bitmap, true) ? data.GetData(DataFormats.Bitmap, true) as BitmapSource : null;
    }

    private void ChooseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加附件", Filter = "所有文件|*.*", Multiselect = true, RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) == true)
            ImportFiles(dialog.FileNames);
    }

    private void OnDrag(object sender, DragEventArgs e)
    {
        bool allow = !busy && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = allow ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        dropArea.SetResourceReference(Border.BorderBrushProperty, allow ? "FocusBrush" : "BorderBrush");
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        dropArea.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths != null)
            ImportFiles(paths);
        e.Handled = true;
    }

    private async void ImportFiles(string[] paths)
    {
        if (busy || !HasRoom() || paths.Length == 0)
            return;
        var added = new List<Attachment>();
        var failures = new List<string>();
        int remaining = MaxAttachments - attachments.Count;
        busy = true;
        save.IsEnabled = false;
        addFiles.IsEnabled = false;
        feedback.Text = "正在复制附件…";
        try
        {
            await Task.Run(delegate
            {
                foreach (string path in paths)
                {
                    if (added.Count >= remaining)
                        break;
                    try
                    {
                        if (Directory.Exists(path))
                            throw new IOException("文件夹可以先压缩成 ZIP。");
                        added.Add(store.AddFile(path));
                    }
                    catch (Exception ex)
                    {
                        failures.Add(Path.GetFileName(path) + "  " + ex.Message);
                    }
                }
            });
            int before = attachments.Count;
            foreach (var attachment in added)
                AddToDraft(attachment);
            RenderAttachments();
            feedback.Text = "新增 " + (attachments.Count - before) + " 个附件";
            if (added.Count + failures.Count < paths.Length)
                feedback.Text += "，每条最多 100 个附件";
            if (failures.Count > 0)
                MessageBox.Show(this, String.Join("\r\n", failures.Take(6)), "部分附件无法添加", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PocketUi.Error(this, "无法添加附件。", ex);
        }
        finally
        {
            busy = false;
            save.IsEnabled = true;
            addFiles.IsEnabled = true;
        }
    }

    private bool HasRoom()
    {
        if (attachments.Count < MaxAttachments)
            return true;
        feedback.Text = "每条问题最多添加 100 个附件";
        return false;
    }

    private void AddToDraft(Attachment attachment)
    {
        if (!attachments.Any(item => item.Hash == attachment.Hash && item.Name == attachment.Name))
            attachments.Add(attachment);
    }

    private void RenderAttachments()
    {
        var panel = Find<StackPanel>("Attachments");
        panel.Children.Clear();
        foreach (var attachment in attachments)
            panel.Children.Add(AttachmentUi.Card(this, store, attachment, true, delegate
            {
                attachments.Remove(attachment);
                RenderAttachments();
                feedback.Text = "已从草稿移除附件";
            }));
        Find<TextBlock>("DropHint").Visibility = attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Commit()
    {
        if (busy || saved)
            return;
        string description = body.Text.Trim();
        string resultText = result.Text.Trim();
        string issueTitle = title.Text.Trim();
        if (issueTitle.Length == 0 && description.Length == 0 && resultText.Length == 0 && attachments.Count == 0)
        {
            feedback.Text = "写点内容，或添加一个附件";
            Keyboard.Focus(body);
            return;
        }
        if (issueTitle.Length == 0)
        {
            string text = description.Length > 0 ? description : resultText;
            issueTitle = text.Length == 0 ? attachments[0].Name : text.Split(new[] { '\r', '\n' }, 2)[0].TrimEnd();
        }
        if (issueTitle.Length > 160)
            issueTitle = issueTitle.Substring(0, Char.IsHighSurrogate(issueTitle[159]) ? 159 : 160);
        DateTime now = DateTime.UtcNow;
        var issue = original == null ? new Issue { Id = Guid.NewGuid().ToString("N"), CreatedUtc = now } : original.Copy();
        issue.Title = issueTitle;
        issue.Body = description;
        issue.Result = resultText.Length == 0 ? null : resultText;
        issue.Category = String.IsNullOrWhiteSpace(category.Text) ? IssueStore.DefaultCategory : category.Text.Trim();
        issue.Resolved = resolved.IsChecked == true;
        issue.Touch(now);
        issue.Attachments = new List<Attachment>(attachments);
        try { saveIssue(issue); }
        catch (Exception ex)
        {
            PocketUi.Error(this, "无法保存问题。", ex);
            return;
        }
        saved = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (saved)
            return;
        if (busy)
        {
            feedback.Text = "请等附件复制完成";
            e.Cancel = true;
            return;
        }
        bool changed = original == null
            ? title.Text.Length > 0 || body.Text.Length > 0 || result.Text.Length > 0 || attachments.Count > 0 ||
                category.Text != initialCategory || resolved.IsChecked == true
            : title.Text != original.Title || body.Text != original.Body || result.Text != (original.Result ?? "") ||
                category.Text != original.Category || (resolved.IsChecked == true) != original.Resolved ||
                !attachments.SequenceEqual(original.Attachments);
        if (changed)
            e.Cancel = MessageBox.Show(this, "放弃这次尚未保存的修改？", "关闭记录窗口",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
    }
}
