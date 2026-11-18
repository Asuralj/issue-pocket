using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("问题随手记")]
[assembly: AssemblyProduct("问题随手记")]
[assembly: AssemblyDescription("Windows 本地问题记录工具")]
[assembly: AssemblyVersion("2.2.4.0")]
[assembly: AssemblyFileVersion("2.2.4.0")]

internal static class PocketProgram
{
    private const string VersionMutex = PocketUi.AppId + ".2.2.4";

    [STAThread]
    private static void Main(string[] args)
    {
        EmbeddedDependencies.Register();
        bool inTray = args.Length == 1 && args[0] == StartupRegistration.Argument;
        bool first;
        using (var mutex = new Mutex(true, PocketUi.AppId, out first))
        using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, PocketUi.AppId + ".Show"))
        {
            if (!first)
            {
                if (inTray)
                    return;
                Mutex runningNewVersion;
                if (Mutex.TryOpenExisting(VersionMutex, out runningNewVersion))
                {
                    using (runningNewVersion)
                        activate.Set();
                }
                else
                    MessageBox.Show("另一个版本的问题随手记正在运行。请从系统托盘退出后，再启动新版。", PocketUi.AppName,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (var version = new Mutex(false, VersionMutex))
            {
                try
                {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PocketUi.AppName);
                    var store = new IssueStore(data);
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    PocketUi.Initialize(store.Root);
                    var window = new DeskWindow(store);
                    var waiter = ThreadPool.RegisterWaitForSingleObject(activate, delegate
                    {
                        window.Dispatcher.BeginInvoke(new Action(window.ShowMain));
                    }, null, Timeout.Infinite, false);
                    try
                    {
                        app.MainWindow = window;
                        window.Start(inTray);
                        app.Run();
                    }
                    finally { waiter.Unregister(null); }
                }
                catch (Exception error)
                {
                    MessageBox.Show("无法启动问题随手记。\r\n\r\n" + error.Message, PocketUi.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
