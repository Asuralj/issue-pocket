# IssuePocket

[简体中文](README.md)

IssuePocket is a local Windows app for logging software development issues, reproduction steps, progress updates, and fixes. Attach screenshots and logs to an issue, then find it later by keyword, category, or status.

The app interface is in Simplified Chinese. This guide includes the Chinese labels you will see on screen.

## Download and run

1. Open the [v2.2.4 release](https://github.com/Asuralj/issue-pocket/releases/tag/v2.2.4) and download `IssuePocket-v2.2.4.exe` under **Assets**.
2. Save the EXE in a folder you can find easily, then double-click it. The app requires Windows with .NET Framework. This version was built on Windows 11.
3. If an older version is already running, right-click the IssuePocket icon in the system tray and choose **退出** (Exit) before opening the new version.

## Log and track issues

1. Click **新建问题** (New Issue) in the main window, or press `Ctrl + Alt + J` while the app is running. If you leave the title blank, IssuePocket uses the first line of the description.
2. Write what happened and how to reproduce it in **问题描述** (Description). Enter or select a category and leave the status as **待处理** (Open) if the issue still needs work. Click **保存问题** (Save Issue) when done.
3. Select the saved issue in the main window. Click **追加跟进** (Add Follow-up) to log progress and **填写结果** (Enter Resolution) to record the fix. You can edit existing follow-ups individually.
4. Click **标为已解决** (Mark as Resolved) when the issue is fixed. If it comes back, click **恢复为待处理** (Reopen).

Use the left panel to filter by status or category and to create categories. The search box matches titles, descriptions, resolutions, follow-ups, categories, and attachment names. You can search within the current filters. If an issue is missing from the results, click **清除筛选** (Clear Filters) and search again.

## Screenshots, attachments, and code

- **Screenshots.** Copy a screenshot and paste it into the issue editor. Click an image in a saved issue to preview it. Scroll to zoom, then hold the left mouse button and drag to pan.
- **Other files.** Click **添加文件…** (Add Files) or drag files into the editor. IssuePocket saves a copy of each attachment. You can open or save a copy of an existing attachment.
- **Code and tables.** Paste multiline code such as XML, or wrap it in triple backticks to create a code block. You can also paste Markdown tables into the description or resolution. Click **预览** (Preview) beside the field to check the formatting.

## Keyboard shortcut and system tray

Click the gear icon in the main window to open **设置** (Settings). Under **快速记录快捷键** (Quick Capture Shortcut), click the shortcut button, press a key or key combination, then click **应用** (Apply). If the shortcut is already in use, choose a different one. Turn on **开机自启** (Start with Windows) to start IssuePocket in the system tray when you sign in.

Closing the main window hides it in the system tray and keeps the shortcut active. Double-click the tray icon to reopen the window. To quit the app, right-click the tray icon and choose **退出** (Exit).

## Back up or move your data

IssuePocket stores data in `%LOCALAPPDATA%\问题随手记` for the current Windows user. To open the folder, choose **数据与备份** (Data and Backup), then **打开数据文件夹** (Open Data Folder). Your issues and attachments stay on the original computer if you only copy the EXE.

1. On the original computer, choose **数据与备份** (Data and Backup), then **备份到 ZIP…** (Back Up to ZIP) and save the file.
2. Copy the ZIP to the new computer. Open IssuePocket and choose **数据与备份** (Data and Backup), then **从备份导入…** (Import from Backup).
3. Import adds new issues and skips existing ones. Check a few issues and attachments after importing, then set up your shortcut and startup preferences on the new computer.

## Build from source

Download or clone this repository, open PowerShell on Windows, and run the following command from the repository directory. The script uses the C# compiler and WPF libraries from Windows .NET Framework. Choose an output path where the `.exe` file does not already exist. `-ExecutionPolicy Bypass` applies only to the PowerShell process started by this command.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\源码\打包新版.ps1' -OutputPath '.\IssuePocket-v2.2.4.exe'
```

The app entry point and user interface are in `源码/现代界面`. The storage and search code is in `源码`. The bundled HtmlAgilityPack library reads HTML from the clipboard and is embedded in the EXE during the build.

## License

**The source code and EXE are for noncommercial use only.** You may use, modify, and share them free of charge for noncommercial purposes. Include the full license and retain the copyright, license, and third-party notices when sharing a copy. If you share a modified version, identify your changes and keep the parts derived from IssuePocket under the same license.

Commercial use requires separate written permission from the rights holder. This includes internal business use, work on commercial projects, paid services, sales or paid distribution, and integration into commercial products or services. See the full terms in the [LICENSE](LICENSE). HtmlAgilityPack is covered by its own [MIT license](源码/第三方/HtmlAgilityPack/LICENSE.txt).
