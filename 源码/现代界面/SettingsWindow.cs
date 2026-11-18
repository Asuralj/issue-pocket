using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal sealed class SettingsWindow : Window
{
    private readonly Grid page;
    private readonly StartupRegistration startup;
    private readonly Func<HotkeyGesture, bool> changeShortcut;
    private readonly ShortcutCapture shortcutCapture;
    private readonly TextBlock shortcutFeedback;
    private readonly ToggleButton startupToggle;
    private readonly TextBlock startupFeedback;
    private bool startupEnabled;

    internal SettingsWindow(Window owner, StartupRegistration startup, HotkeyGesture preferred,
                            HotkeyGesture current, Func<HotkeyGesture, bool> changeShortcut)
    {
        this.startup = startup;
        this.changeShortcut = changeShortcut;
        PocketUi.Window(this, "设置", 476, 391);
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        page = (Grid)PocketUi.Load("Settings.xaml");
        Content = page;
        shortcutFeedback = Find<TextBlock>("ShortcutFeedback");
        startupToggle = Find<ToggleButton>("StartupToggle");
        startupFeedback = Find<TextBlock>("StartupFeedback");
        var applyShortcut = Find<Button>("ApplyShortcut");
        shortcutCapture = new ShortcutCapture(Find<Button>("ShortcutCapture"), applyShortcut, shortcutFeedback,
            preferred ?? current ?? HotkeyGesture.Default);
        ShowActiveShortcut(current);
        if (preferred != null && !preferred.Matches(current))
            shortcutFeedback.Text = "已保存的快捷键当前不可用，请调整后应用。";
        applyShortcut.Click += delegate { ApplyShortcut(); };
        try
        {
            startupEnabled = startup.IsEnabled();
            startupToggle.IsChecked = startupEnabled;
            startupToggle.Click += delegate { ApplyStartup(); };
        }
        catch (Exception error)
        {
            startupToggle.IsEnabled = false;
            startupFeedback.Text = "无法读取启动项：" + error.Message;
        }
    }

    private T Find<T>(string name) where T : FrameworkElement
    {
        return (T)page.FindName(name);
    }

    private void ShowActiveShortcut(HotkeyGesture current)
    {
        Find<TextBlock>("ActiveShortcut").Text = current == null ? "当前没有可用的全局快捷键" : "当前生效  " + current;
    }

    private void ApplyShortcut()
    {
        var gesture = shortcutCapture.Value;
        if (gesture == null)
            return;
        try
        {
            if (!changeShortcut(gesture))
            {
                shortcutFeedback.Text = "该组合已被占用或由系统保留，请换一组快捷键。";
                return;
            }
            ShowActiveShortcut(gesture);
            shortcutFeedback.Text = "已应用，下次启动也会使用这个设置。";
        }
        catch (Exception error)
        {
            shortcutFeedback.Text = "无法保存快捷键：" + error.Message;
        }
    }

    internal void RecordRegisteredShortcut(HotkeyGesture gesture)
    {
        shortcutCapture.Record(gesture);
    }

    private void ApplyStartup()
    {
        bool enabled = startupToggle.IsChecked == true;
        try
        {
            startup.SetEnabled(enabled);
            startupEnabled = enabled;
            startupFeedback.Text = enabled ? "已开启，登录 Windows 后自动驻留托盘。" : "已关闭开机自启。";
        }
        catch (Exception error)
        {
            startupToggle.IsChecked = startupEnabled;
            startupFeedback.Text = "无法更改启动项：" + error.Message;
        }
    }
}
