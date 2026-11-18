using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PocketUi
{
    internal const string AppName = "问题随手记";
    internal const string AppId = "IssuePocket.8f4c5358812c4700b49e6217cc8b60ad";
    internal static bool Dark { get; private set; }

    private static readonly string[,] Palette =
    {
        { "CanvasBrush",   "#F5F6FA", "#171A23" },
        { "NavBrush",      "#ECEFF7", "#141720" },
        { "SurfaceBrush",  "#FFFFFF", "#222632" },
        { "PanelBrush",    "#F7F8FC", "#282D3B" },
        { "Ink",           "#1B2235", "#EEF0F8" },
        { "Muted",         "#56617A", "#ABB4CC" },
        { "BorderBrush",   "#DDE2ED", "#373E50" },
        { "ScrollThumb",   "#AAB4CA", "#5F6D8C" },
        { "PrimaryBrush",  "#4953B7", "#555FCA" },
        { "AccentText",    "#414AAB", "#BDC4FF" },
        { "FocusBrush",    "#6571D5", "#9BA7FF" },
        { "SelectedBrush", "#ECEFFD", "#31395A" },
        { "SelectedBorder","#CED5F6", "#4B588B" },
        { "HoverOverlay",  "#15192238", "#1EFFFFFF" },
        { "OpenFill",      "#FEF2DB", "#493926" },
        { "OpenInk",       "#82530A", "#FFD08A" },
        { "DoneFill",      "#DEF5EB", "#223E35" },
        { "DoneInk",       "#21674E", "#9DE3C6" }
    };

    internal static void Initialize(string directory)
    {
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)Load("Theme.xaml"));
        string path = Path.Combine(directory, "theme");
        try { Dark = File.Exists(path) && File.ReadAllText(path).Trim() == "dark"; }
        catch (IOException) { Dark = false; }
        catch (UnauthorizedAccessException) { Dark = false; }
        ApplyTheme(Dark);
    }

    internal static void SetTheme(string directory, bool dark)
    {
        string path = Path.Combine(directory, "theme");
        string pending = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".theme");
        File.WriteAllText(pending, dark ? "dark" : "light");
        if (File.Exists(path))
            File.Replace(pending, path, path + ".bak", true);
        else
            File.Move(pending, path);
        ApplyTheme(dark);
    }

    private static void ApplyTheme(bool dark)
    {
        Dark = dark;
        for (int index = 0; index < Palette.GetLength(0); index++)
        {
            var color = (Color)ColorConverter.ConvertFromString(Palette[index, dark ? 2 : 1]);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[Palette[index, 0]] = brush;
        }
        foreach (Window window in Application.Current.Windows)
            UpdateCaption(window);
    }

    internal static object Load(string resource)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pocket." + resource))
        {
            if (stream == null)
                throw new InvalidOperationException("缺少界面资源 " + resource);
            return XamlReader.Load(stream);
        }
    }

    internal static void Window(Window window, string title, double width, double height)
    {
        window.Title = title;
        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SetResourceReference(Control.FontFamilyProperty, "AppFont");
        window.SetResourceReference(Control.ForegroundProperty, "Ink");
        window.SetResourceReference(Control.BackgroundProperty, "CanvasBrush");
        window.FontSize = 14;
        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location))
        {
            if (icon != null)
                window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        window.SourceInitialized += delegate { UpdateCaption(window); };
    }

    private static void UpdateCaption(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        int enabled = Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    internal static void Error(Window owner, string message, Exception error)
    {
        MessageBox.Show(owner, message + "\r\n\r\n" + error.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal static MenuItem AddMenu(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += delegate { action(); };
        menu.Items.Add(item);
        return item;
    }

    internal static void OpenMenu(ContextMenu menu, UIElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
