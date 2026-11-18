using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

internal sealed class DeskWindow : Window
{
    private static readonly RoutedCommand EditFollowUpCommand = new RoutedCommand();
    private const string PagesIcon = "M1,2 H13 V4 H1 Z M1,6 H13 V8 H1 Z M1,10 H10 V12 H1 Z";
    private const string PendingIcon = "M7,0 A7,7 0 1 0 7.01,0 Z M7,1.6 A5.4,5.4 0 1 1 6.99,1.6 Z M6.2,3.4 H7.8 V6.5 L10.3,8 L9.5,9.3 L6.2,7.4 Z";
    private const string ResolvedIcon = "M7,0 A7,7 0 1 0 7.01,0 Z M7,1.6 A5.4,5.4 0 1 1 6.99,1.6 Z M3.5,7.2 L4.6,6.1 L6.2,7.7 L9.5,4.4 L10.6,5.5 L6.2,9.9 Z";
    private const string FolderIcon = "M0,3 Q0,1 2,1 H5 L7,3 H12 Q14,3 14,5 V11 Q14,13 12,13 H2 Q0,13 0,11 Z M1.6,6 V11 Q1.6,11.4 2,11.4 H12 Q12.4,11.4 12.4,11 V6 Z";
    private readonly IssueStore store;
    private readonly StartupRegistration startup;
    private readonly HotkeySettings hotkeySettings;
    private readonly Grid page;
    private readonly ListBox views;
    private readonly ListBox categories;
    private readonly ListBox records;
    private readonly TextBox search;
    private readonly TextBlock feedback;
    private readonly Button dataButton;
    private readonly Button newButton;
    private readonly Button editButton;
    private readonly Button resultButton;
    private readonly Button toggleButton;
    private readonly Button followUpButton;
    private readonly Button newCategoryButton;
    private List<Issue> issues;
    private List<string> registeredCategories;
    private Issue renderedIssue;
    private QuickWindow editor;
    private SettingsWindow settings;
    private Forms.NotifyIcon tray;
    private System.Drawing.Icon trayIcon;
    private HwndSource nativeWindow;
    private GlobalHotkey hotkey;
    private HotkeyGesture preferredShortcut;
    private bool filtering;
    private bool transferring;
    private bool exiting;

    internal DeskWindow(IssueStore store, StartupRegistration startup = null)
    {
        this.store = store;
        this.startup = startup ?? new StartupRegistration(System.Reflection.Assembly.GetExecutingAssembly().Location);
        hotkeySettings = new HotkeySettings(store.Root);
        preferredShortcut = hotkeySettings.Load();
        int unreadable;
        issues = store.Load(out unreadable);
        registeredCategories = store.Categories.Load();
        PocketUi.Window(this, PocketUi.AppName, 1235, 760);
        MinWidth = 930;
        MinHeight = 600;
        page = (Grid)PocketUi.Load("Desk.xaml");
        Content = page;
        views = Find<ListBox>("Views");
        categories = Find<ListBox>("Categories");
        records = Find<ListBox>("Records");
        search = Find<TextBox>("Search");
        feedback = Find<TextBlock>("Feedback");
        newButton = Find<Button>("NewButton");
        dataButton = Find<Button>("DataButton");
        editButton = Find<Button>("Edit");
        resultButton = Find<Button>("EditResult");
        toggleButton = Find<Button>("ToggleStatus");
        followUpButton = Find<Button>("AddFollowUp");
        newCategoryButton = Find<Button>("NewCategory");

        newButton.Click += delegate { OpenEditor(null, false, false); };
        editButton.Click += delegate { EditSelection(false); };
        resultButton.Click += delegate { EditSelection(true); };
        toggleButton.Click += delegate { ToggleSelection(); };
        followUpButton.Click += delegate { AppendFollowUp(); };
        CommandBindings.Add(new CommandBinding(EditFollowUpCommand,
            (sender, args) => EditFollowUp((IssueFollowUp)args.Parameter),
            (sender, args) => args.CanExecute = followUpButton.IsEnabled && args.Parameter is IssueFollowUp));
        newCategoryButton.Click += delegate { CreateCategory(this); };
        Find<Button>("SettingsButton").Click += delegate { OpenSettings(); };
        var themeButton = Find<Button>("ThemeButton");
        themeButton.Click += delegate
        {
            try
            {
                PocketUi.SetTheme(store.Root, !PocketUi.Dark);
                UpdateThemeButton();
            }
            catch (Exception ex) { PocketUi.Error(this, "无法保存主题设置。", ex); }
        };
        UpdateThemeButton();
        var dataMenu = new ContextMenu();
        PocketUi.AddMenu(dataMenu, "备份到 ZIP…", ExportBackup);
        PocketUi.AddMenu(dataMenu, "从备份导入…", ImportBackup);
        dataMenu.Items.Add(new Separator());
        PocketUi.AddMenu(dataMenu, "打开数据文件夹", delegate
        {
            try { Process.Start(new ProcessStartInfo(store.Root) { UseShellExecute = true }); }
            catch (Exception ex) { PocketUi.Error(this, "无法打开数据文件夹。", ex); }
        });
        dataButton.Click += delegate { PocketUi.OpenMenu(dataMenu, dataButton); };

        var clearSearch = Find<Button>("ClearSearch");
        var searchPlaceholder = Find<TextBlock>("SearchPlaceholder");
        search.TextChanged += delegate
        {
            clearSearch.Visibility = search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            searchPlaceholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshList();
        };
        clearSearch.Click += delegate { search.Clear(); Keyboard.Focus(search); };
        Find<Button>("ResetFilters").Click += delegate { ResetFilters(); };
        views.SelectionChanged += delegate { RefreshList(); };
        categories.SelectionChanged += delegate { RefreshList(); };
        records.SelectionChanged += delegate { if (!filtering) ShowSelection(); };
        records.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left && Selected != null &&
                ItemsControl.ContainerFromElement(records, args.OriginalSource as DependencyObject) is ListBoxItem)
                EditSelection(false);
        };
        records.KeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Enter && Selected != null)
            {
                args.Handled = true;
                EditSelection(false);
            }
        };
        PreviewKeyDown += OnKey;
        RefreshNavigation();
        RefreshList();
        if (unreadable > 0)
            feedback.Text = unreadable + " 条记录无法读取，源文件仍保存在数据文件夹。";
    }

    private T Find<T>(string name) where T : FrameworkElement
    {
        return (T)page.FindName(name);
    }

    private Issue Selected
    {
        get { return records.SelectedItem == null ? null : ((RecordRow)records.SelectedItem).Issue; }
    }

    private string SelectedCategory
    {
        get { return categories.SelectedIndex > 0 ? ((NavigationRow)categories.SelectedItem).Name : null; }
    }

    private void UpdateThemeButton()
    {
        Find<Button>("ThemeButton").Content = PocketUi.Dark ? "切换到浅色" : "切换到深色";
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (args.Key == Key.F)
        {
            Keyboard.Focus(search);
            search.SelectAll();
            args.Handled = true;
        }
        else if (args.Key == Key.N)
        {
            OpenEditor(null, false, false);
            args.Handled = true;
        }
    }

    private void ResetFilters()
    {
        filtering = true;
        search.Clear();
        views.SelectedIndex = 0;
        categories.SelectedIndex = 0;
        filtering = false;
        RefreshList();
    }

    private IEnumerable<string> CategoryNames()
    {
        return registeredCategories.Concat(issues.Select(issue => issue.Category)).Concat(new[] { IssueStore.DefaultCategory })
            .Distinct(CategoryCatalog.Comparer).OrderBy(name => name, CategoryCatalog.Comparer);
    }

    private string CreateCategory(Window owner)
    {
        string name = TextEntryWindow.Prompt(owner, "新建分类", "分类名称", CategoryCatalog.MaxNameLength);
        if (name == null)
            return null;
        string existing = CategoryNames().FirstOrDefault(candidate => CategoryCatalog.Comparer.Equals(name, candidate));
        try
        {
            if (existing == null)
                registeredCategories = store.Categories.Merge(new[] { name });
            else
                name = existing;
            RefreshNavigation(owner == this ? name : null);
            RefreshList();
            feedback.Text = existing == null ? "已新建分类「" + name + "」" : "已选用分类「" + name + "」";
            return name;
        }
        catch (Exception ex)
        {
            PocketUi.Error(owner, "无法新建分类。", ex);
            return null;
        }
    }

    private void RefreshNavigation(string categoryToSelect = null)
    {
        int state = Math.Max(0, views.SelectedIndex);
        string currentCategory = categoryToSelect ?? SelectedCategory;
        int done = issues.Count(issue => issue.Resolved);
        filtering = true;
        views.ItemsSource = new[]
        {
            new NavigationRow("全部问题", issues.Count, PagesIcon),
            new NavigationRow("待处理", issues.Count - done, PendingIcon),
            new NavigationRow("已解决", done, ResolvedIcon)
        };
        views.SelectedIndex = state;
        var counts = issues.GroupBy(issue => issue.Category, CategoryCatalog.Comparer)
            .ToDictionary(group => group.Key, group => group.Count(), CategoryCatalog.Comparer);
        var groups = new List<NavigationRow>();
        foreach (string name in CategoryNames())
        {
            int count;
            counts.TryGetValue(name, out count);
            if (count > 0 || !CategoryCatalog.Comparer.Equals(name, IssueStore.DefaultCategory) ||
                CategoryCatalog.Comparer.Equals(name, currentCategory))
                groups.Add(new NavigationRow(name, count, FolderIcon));
        }
        groups.Insert(0, new NavigationRow("全部分类", issues.Count, FolderIcon));
        categories.ItemsSource = groups;
        categories.SelectedIndex = Math.Max(0, groups.FindIndex(1, groups.Count - 1,
            row => CategoryCatalog.Comparer.Equals(row.Name, currentCategory)));
        filtering = false;
    }

    private void RefreshList(string preferId = null)
    {
        if (filtering)
            return;
        var current = Selected;
        string id = preferId ?? (current == null ? null : current.Id);
        var terms = IssueSearch.Terms(search.Text);
        bool? state = views.SelectedIndex > 0 ? (bool?)(views.SelectedIndex == 2) : null;
        var found = IssueSearch.Match(issues, terms, SelectedCategory, state)
            .Select(issue => new RecordRow(issue, terms)).ToList();
        filtering = true;
        records.ItemsSource = found;
        if (found.Count > 0)
            records.SelectedIndex = Math.Max(0, found.FindIndex(row => row.Issue.Id == id));
        filtering = false;
        var activeView = views.SelectedItem as NavigationRow;
        Find<TextBlock>("ViewTitle").Text = SelectedCategory ?? (activeView == null ? "全部问题" : activeView.Name);
        Find<TextBlock>("ListCount").Text = found.Count + " 条记录";
        ShowSelection();
    }

    private void ShowSelection()
    {
        var issue = Selected;
        if (issue != null && Object.ReferenceEquals(issue, renderedIssue))
        {
            UpdateActions();
            return;
        }
        renderedIssue = issue;
        bool found = issue != null;
        Find<Grid>("EmptyState").Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        Find<Grid>("Detail").Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        if (!found)
        {
            string category = SelectedCategory;
            bool hasRecords = category == null ? issues.Count > 0 :
                issues.Any(item => CategoryCatalog.Comparer.Equals(item.Category, category));
            Find<TextBlock>("EmptyTitle").Text = hasRecords ? "没有匹配的问题" : "还没有问题";
            Find<TextBlock>("EmptyText").Text = hasRecords ? "换个关键词，或清除当前筛选。" : "点击「新建问题」开始记录。";
            Find<Button>("ResetFilters").Visibility = hasRecords ? Visibility.Visible : Visibility.Collapsed;
            Find<WrapPanel>("Attachments").Children.Clear();
            Find<ItemsControl>("FollowUps").ItemsSource = null;
            return;
        }
        Find<TextBlock>("IssueTitle").Text = issue.Title;
        Find<Border>("DetailPill").DataContext = new RecordRow(issue, new string[0]);
        Find<TextBlock>("DetailState").Text = issue.Resolved ? "已解决" : "待处理";
        Find<TextBlock>("IssueCategory").Text = issue.Category;
        Find<TextBlock>("IssueDate").Text = issue.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Find<ContentControl>("IssueBody").Content = MarkdownPreview.Create(issue.Body.Length > 0 ? issue.Body : "未填写描述", "问题描述");
        bool hasResult = !String.IsNullOrWhiteSpace(issue.Result);
        var result = Find<ContentControl>("IssueResult");
        result.Content = hasResult ? MarkdownPreview.Create(issue.Result, "处理结果") : null;
        result.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        Find<TextBlock>("EmptyResult").Visibility = hasResult ? Visibility.Collapsed : Visibility.Visible;
        resultButton.Content = hasResult ? "编辑结果" : "填写结果";
        toggleButton.Content = issue.Resolved ? "恢复为待处理" : "标为已解决";
        var attachments = Find<WrapPanel>("Attachments");
        attachments.Children.Clear();
        foreach (var attachment in issue.Attachments)
            attachments.Children.Add(AttachmentUi.Card(this, store, attachment, false));
        Find<TextBlock>("AttachmentsHeading").Text = "附件 · " + issue.Attachments.Count;
        Find<StackPanel>("AttachmentArea").Visibility = issue.Attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var followUps = issue.FollowUps.Reverse().Select(entry => new FollowUpRow(entry)).ToList();
        Find<ItemsControl>("FollowUps").ItemsSource = followUps;
        Find<TextBlock>("EmptyFollowUps").Visibility = followUps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Find<ScrollViewer>("DetailScroll").ScrollToTop();
        UpdateActions();
    }

    private void UpdateActions()
    {
        newButton.IsEnabled = !transferring;
        editButton.IsEnabled = !transferring;
        resultButton.IsEnabled = !transferring;
        var issue = Selected;
        bool canUpdate = !transferring && issue != null && (editor == null || !editor.IsEditing(issue.Id));
        toggleButton.IsEnabled = canUpdate;
        if (followUpButton.IsEnabled != canUpdate)
        {
            followUpButton.IsEnabled = canUpdate;
            CommandManager.InvalidateRequerySuggested();
        }
        newCategoryButton.IsEnabled = !transferring;
        dataButton.IsEnabled = !transferring && editor == null;
    }

    private void EditSelection(bool result)
    {
        if (Selected != null)
            OpenEditor(Selected, false, result);
    }

    private void OpenEditor(Issue issue, bool quick, bool result)
    {
        if (transferring || settings != null)
            return;
        if (editor != null)
        {
            if (editor.WindowState == WindowState.Minimized)
                editor.WindowState = WindowState.Normal;
            editor.Activate();
            if (result && issue != null && editor.IsEditing(issue.Id))
                editor.RevealResult(true);
            return;
        }
        editor = new QuickWindow(store, issue, quick ? null : SelectedCategory, CategoryNames, CreateCategory, Persist, result);
        editor.Closed += delegate { editor = null; UpdateActions(); };
        if (quick)
            editor.Topmost = true;
        else
        {
            editor.Owner = this;
            editor.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        editor.Show();
        editor.Activate();
        UpdateActions();
    }

    private void ToggleSelection()
    {
        if (Selected == null || !toggleButton.IsEnabled)
            return;
        var issue = Selected.Copy();
        issue.Resolved = !issue.Resolved;
        issue.Touch(DateTime.UtcNow);
        try
        {
            Persist(issue);
            feedback.Text = issue.Resolved ? "已标为已解决" : "已恢复为待处理";
        }
        catch (Exception ex) { PocketUi.Error(this, "无法更新状态。", ex); }
    }

    private void AppendFollowUp()
    {
        var selected = Selected;
        if (selected == null || !followUpButton.IsEnabled)
            return;
        string text = TextEntryWindow.Prompt(this, "追加跟进", "本次进展", IssueFollowUp.MaxLength, multiline: true);
        if (text == null)
            return;
        var issue = selected.Copy();
        issue.AddFollowUp(text, DateTime.UtcNow);
        try
        {
            Persist(issue);
            Find<TextBlock>("FollowUpHeading").BringIntoView();
            feedback.Text = "已追加跟进";
        }
        catch (Exception ex) { PocketUi.Error(this, "无法保存跟进。", ex); }
    }

    private void EditFollowUp(IssueFollowUp entry)
    {
        var selected = Selected;
        if (selected == null || !followUpButton.IsEnabled)
            return;
        string text = TextEntryWindow.Prompt(this, "编辑跟进", "跟进内容", IssueFollowUp.MaxLength,
            multiline: true, initialText: entry.Text);
        if (text == null || text == entry.Text)
            return;

        var issue = selected.Copy();
        if (!issue.EditFollowUp(entry, text, DateTime.UtcNow))
            return;
        var detail = Find<ScrollViewer>("DetailScroll");
        double offset = detail.VerticalOffset;
        try
        {
            Persist(issue);
            if (Selected != null && Selected.Id == issue.Id)
                detail.ScrollToVerticalOffset(offset);
            feedback.Text = "已修改跟进";
        }
        catch (Exception ex) { PocketUi.Error(this, "无法修改跟进。", ex); }
    }

    private void Persist(Issue issue)
    {
        store.Save(issue);
        int index = issues.FindIndex(item => item.Id == issue.Id);
        if (index < 0)
            issues.Add(issue);
        else
            issues[index] = issue;
        RefreshNavigation();
        RefreshList(issue.Id);
        feedback.Text = "已保存";
    }

    private async void ExportBackup()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "问题随手记备份_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip",
            Filter = "问题随手记备份|*.zip", DefaultExt = "zip", AddExtension = true,
            Title = "保存备份", RestoreDirectory = true, OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        string path = dialog.FileName;
        if (File.Exists(path))
        {
            PocketUi.Error(this, "无法保存备份。", new IOException("目标文件已存在，请换一个文件名。"));
            return;
        }
        await Transfer("正在备份问题数据…", delegate
        {
            store.ExportBackup(path);
            return "备份已保存到 " + path;
        }, false);
    }

    private async void ImportBackup()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "问题随手记备份|*.zip", Title = "导入备份", RestoreDirectory = true };
        if (dialog.ShowDialog(this) != true)
            return;
        string path = dialog.FileName;
        await Transfer("正在导入备份…", delegate
        {
            int skipped;
            int added = store.ImportBackup(path, out skipped);
            int newCategories = store.Categories.Load().Count - registeredCategories.Count;
            string message = "导入 " + added + " 条记录，跳过 " + skipped + " 条本机已有记录";
            return newCategories > 0 ? message + "，新增 " + newCategories + " 个分类" : message;
        }, true);
    }

    private async Task Transfer(string progress, Func<string> operation, bool reload)
    {
        transferring = true;
        UpdateActions();
        feedback.Text = progress;
        string message;
        try { message = await Task.Run(operation); }
        catch (Exception ex)
        {
            PocketUi.Error(this, "数据操作失败。", ex);
            message = "数据操作失败";
        }
        finally
        {
            transferring = false;
            UpdateActions();
        }
        if (reload)
        {
            int unreadable;
            issues = store.Load(out unreadable);
            registeredCategories = store.Categories.Load();
            RefreshNavigation();
            RefreshList();
        }
        feedback.Text = message;
    }

    internal void ShowMain()
    {
        if (settings != null)
        {
            settings.Activate();
            return;
        }
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    internal void Start(bool inTray)
    {
        if (inTray)
        {
            new WindowInteropHelper(this).EnsureHandle();
            return;
        }
        try { startup.RefreshPath(); }
        catch (Exception error) { feedback.Text = "无法更新启动项：" + error.Message; }
        Show();
    }

    private void OpenSettings()
    {
        if (settings != null)
        {
            settings.Activate();
            return;
        }
        settings = new SettingsWindow(this, startup, preferredShortcut, hotkey.Current, ChangeShortcut);
        try { settings.ShowDialog(); }
        finally { settings = null; }
    }

    private bool ChangeShortcut(HotkeyGesture requested)
    {
        if (!hotkey.TryChange(requested, delegate { hotkeySettings.Save(requested); }))
            return false;
        preferredShortcut = requested;
        UpdateShortcutLabels();
        return true;
    }

    private void UpdateShortcutLabels()
    {
        var current = hotkey.Current;
        Find<TextBlock>("ShortcutHint").Text = current == null ? "可从系统托盘新建问题" : current + "  快速记录";
        tray.ContextMenuStrip.Items[0].Text = current == null ? "新建问题" : "新建问题  " + current;
    }

    internal void ExitApp()
    {
        if (transferring)
        {
            ShowMain();
            MessageBox.Show(this, "请等数据操作完成后再退出。", Title);
            return;
        }
        if (editor != null)
        {
            editor.Activate();
            editor.Close();
            if (editor != null)
                return;
        }
        exiting = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        nativeWindow = HwndSource.FromHwnd(handle);
        nativeWindow.AddHook(HandleMessage);
        hotkey = new GlobalHotkey(handle);
        hotkey.RegisterInitial(preferredShortcut);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("新建问题", null, delegate { OpenEditor(null, true, false); });
        menu.Items.Add("打开问题随手记", null, delegate { ShowMain(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { ExitApp(); });
        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        tray = new Forms.NotifyIcon { Text = PocketUi.AppName, Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate { ShowMain(); };
        UpdateShortcutLabels();
    }

    private IntPtr HandleMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == GlobalHotkey.Message && hotkey != null && hotkey.IsActive(wparam))
        {
            if (settings != null)
                settings.RecordRegisteredShortcut(hotkey.Current);
            else
                OpenEditor(null, true, false);
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!exiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (hotkey != null)
            hotkey.Dispose();
        if (nativeWindow != null)
            nativeWindow.RemoveHook(HandleMessage);
        if (tray != null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
        }
        if (trayIcon != null)
            trayIcon.Dispose();
        base.OnClosed(e);
    }

    private sealed class NavigationRow
    {
        public string Name { get; private set; }
        public int Count { get; private set; }
        public string Icon { get; private set; }

        internal NavigationRow(string name, int count, string icon)
        {
            Name = name;
            Count = count;
            Icon = icon;
        }
    }

    private sealed class RecordRow
    {
        internal Issue Issue { get; private set; }
        public string Title { get { return Issue.Title; } }
        public string Category { get { return Issue.Category; } }
        public string Date { get { return Issue.UpdatedUtc.ToLocalTime().ToString("MM-dd"); } }
        public bool Resolved { get { return Issue.Resolved; } }
        public string State { get { return Issue.Resolved ? "已解决" : "待处理"; } }
        public string Snippet { get; private set; }

        internal RecordRow(Issue issue, string[] terms)
        {
            Issue = issue;
            Snippet = IssueSearch.Snippet(issue, terms);
        }
    }

    private sealed class FollowUpRow
    {
        public IssueFollowUp Entry { get; private set; }
        public string Date { get { return Entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); } }
        public FrameworkElement Preview { get; private set; }
        public ICommand EditCommand { get { return EditFollowUpCommand; } }

        internal FollowUpRow(IssueFollowUp entry)
        {
            Entry = entry;
            Preview = MarkdownPreview.Create(entry.Text, "跟进内容");
        }
    }

}
