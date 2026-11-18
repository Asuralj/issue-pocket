using System;
using System.IO;
using Microsoft.Win32;

internal sealed class StartupRegistration
{
    internal const string Argument = "--startup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string registryPath;
    private readonly string command;

    internal StartupRegistration(string executable, string registryPath = RunKey)
    {
        this.registryPath = registryPath;
        command = "\"" + Path.GetFullPath(executable) + "\" " + Argument;
    }

    internal bool IsEnabled()
    {
        return !String.IsNullOrEmpty(ReadCommand());
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (command.Length > 260)
                throw new InvalidOperationException("程序路径过长，无法添加到 Windows 启动项。");
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue(PocketUi.AppId, command, RegistryValueKind.String);
        }
        else
        {
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath, true))
                if (key != null)
                    key.DeleteValue(PocketUi.AppId, false);
        }
    }

    internal void RefreshPath()
    {
        string existing = ReadCommand();
        if (!String.IsNullOrEmpty(existing) && !String.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
            SetEnabled(true);
    }

    private string ReadCommand()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
            return key == null ? null : key.GetValue(PocketUi.AppId) as string;
    }
}
