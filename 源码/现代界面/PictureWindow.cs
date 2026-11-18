using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal sealed class PictureWindow : Window
{
    private readonly BitmapSource bitmap;
    private readonly ScrollViewer viewport;
    private readonly Grid stage;
    private readonly Image picture;
    private readonly TextBlock percentage;
    private double zoom = 1;
    private bool fit = true;
    private Point? panOrigin;
    private Vector panOffset;
    private bool dragging;

    internal PictureWindow(BitmapSource bitmap, string name, Action<Window> save)
    {
        this.bitmap = bitmap;
        PocketUi.Window(this, name, 942, 680);
        MinWidth = 455;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Border { Padding = new Thickness(13) };
        root.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        var frame = new Grid();
        root.Child = frame;
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition());
        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        var saveButton = Command("保存原图…", delegate { save(this); });
        DockPanel.SetDock(saveButton, Dock.Right);
        tools.Children.Add(saveButton);
        var zoomTools = new StackPanel { Orientation = Orientation.Horizontal };
        zoomTools.Children.Add(Command("缩小", delegate { ZoomAt(zoom / 1.25, Center()); }));
        zoomTools.Children.Add(Command("放大", delegate { ZoomAt(zoom * 1.25, Center()); }));
        zoomTools.Children.Add(Command("适应窗口", Fit));
        percentage = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        percentage.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        zoomTools.Children.Add(percentage);
        tools.Children.Add(zoomTools);
        frame.Children.Add(tools);

        viewport = new ScrollViewer
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 24, 33)),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        stage = new Grid { Background = Brushes.Transparent };
        picture = new Image
        {
            Source = bitmap, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stage.Children.Add(picture);
        viewport.Content = stage;
        Grid.SetRow(viewport, 1);
        frame.Children.Add(viewport);
        Content = root;
        var menu = new ContextMenu();
        PocketUi.AddMenu(menu, "保存原图…", delegate { save(this); });
        viewport.ContextMenu = menu;
        Loaded += delegate { Fit(); };
        viewport.SizeChanged += delegate { if (fit) Fit(); else PositionPicture(); };
        viewport.ScrollChanged += delegate { UpdatePanCursor(); };
        viewport.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs args)
        {
            ZoomAt(zoom * Math.Pow(1.25, args.Delta / 120d), args.GetPosition(viewport));
            args.Handled = true;
        };
        picture.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ClickCount == 2)
            {
                EndPan();
                ToggleZoom(args.GetPosition(viewport));
                args.Handled = true;
            }
        };
        stage.MouseLeftButtonDown += StartPan;
        stage.MouseMove += MovePan;
        stage.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
        {
            if (!panOrigin.HasValue)
                return;
            EndPan();
            args.Handled = true;
        };
        stage.LostMouseCapture += delegate { ResetPan(); };
        stage.MouseLeave += delegate
        {
            if (!stage.IsMouseCaptured)
                ResetPan();
        };
        PreviewKeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) Close(); };
    }

    private bool CanPan
    {
        get { return !fit && (viewport.ScrollableWidth > 0 || viewport.ScrollableHeight > 0); }
    }

    private void StartPan(object sender, MouseButtonEventArgs args)
    {
        if (!CanPan)
            return;
        panOrigin = args.GetPosition(viewport);
        panOffset = new Vector(viewport.HorizontalOffset, viewport.VerticalOffset);
        stage.CaptureMouse();
        args.Handled = true;
    }

    private void MovePan(object sender, MouseEventArgs args)
    {
        if (!panOrigin.HasValue)
            return;
        if (args.LeftButton == MouseButtonState.Released)
        {
            EndPan();
            return;
        }
        PanTo(args.GetPosition(viewport));
        args.Handled = true;
    }

    private void PanTo(Point point)
    {
        Vector delta = point - panOrigin.Value;
        if (!dragging)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragging = true;
            UpdatePanCursor();
        }
        viewport.ScrollToHorizontalOffset(panOffset.X - delta.X);
        viewport.ScrollToVerticalOffset(panOffset.Y - delta.Y);
    }

    private void EndPan()
    {
        ResetPan();
        if (stage.IsMouseCaptured)
            stage.ReleaseMouseCapture();
    }

    private void ResetPan()
    {
        panOrigin = null;
        dragging = false;
        UpdatePanCursor();
    }

    private void UpdatePanCursor()
    {
        stage.Cursor = dragging ? Cursors.SizeAll : (CanPan ? Cursors.Hand : Cursors.Arrow);
    }

    private static Button Command(string name, Action action)
    {
        var button = new Button { Content = name, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += delegate { action(); };
        return button;
    }

    private Point Center()
    {
        return new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
    }

    private void ToggleZoom(Point anchor)
    {
        if (fit)
            ZoomAt(Math.Max(1, zoom * 2), anchor);
        else
            Fit();
    }

    private void Fit()
    {
        if (viewport.ActualWidth <= 20 || viewport.ActualHeight <= 20)
            return;
        EndPan();
        fit = true;
        viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        zoom = Math.Min(1, Math.Min((viewport.ActualWidth - 20) / bitmap.PixelWidth,
                                    (viewport.ActualHeight - 20) / bitmap.PixelHeight));
        PositionPicture();
        viewport.ScrollToHome();
    }

    private void ZoomAt(double value, Point anchor)
    {
        EndPan();
        Point within = viewport.TranslatePoint(anchor, picture);
        double x = Math.Min(1, Math.Max(0, within.X / Math.Max(1, picture.ActualWidth)));
        double y = Math.Min(1, Math.Max(0, within.Y / Math.Max(1, picture.ActualHeight)));
        zoom = Math.Max(Math.Min(zoom, 0.05), Math.Min(8, value));
        fit = false;
        viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        PositionPicture();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            if (fit)
                return;
            Point current = picture.TranslatePoint(new Point(x * picture.ActualWidth, y * picture.ActualHeight), viewport);
            viewport.ScrollToHorizontalOffset(viewport.HorizontalOffset + current.X - anchor.X);
            viewport.ScrollToVerticalOffset(viewport.VerticalOffset + current.Y - anchor.Y);
        }));
    }

    private void PositionPicture()
    {
        picture.Width = Math.Max(1, bitmap.PixelWidth * zoom);
        picture.Height = Math.Max(1, bitmap.PixelHeight * zoom);
        stage.Width = Math.Max(Math.Max(0, viewport.ActualWidth - (fit ? 0 : 18)), picture.Width + 16);
        stage.Height = Math.Max(Math.Max(0, viewport.ActualHeight - (fit ? 0 : 18)), picture.Height + 16);
        percentage.Text = Math.Max(1, Math.Round(zoom * 100)) + "%";
    }
}
