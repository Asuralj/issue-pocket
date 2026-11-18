using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

internal sealed class TextEntryWindow : Window
{
    private readonly TextBox input;

    private TextEntryWindow(Window owner, string title, string label, int limit, bool multiline, string initialText)
    {
        PocketUi.Window(this, title, multiline ? 630 : 460, multiline ? 408 : 219);
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(19, 15, 19, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });

        var fields = new DockPanel();
        var heading = new TextBlock { Text = label };
        heading.SetResourceReference(StyleProperty, "FieldLabel");
        var fieldBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(fieldBar, Dock.Top);
        fields.Children.Add(fieldBar);
        input = new TextBox
        {
            MaxLength = limit,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
        };
        AutomationProperties.SetName(input, label);
        if (!multiline)
        {
            input.Height = 37;
            input.VerticalAlignment = VerticalAlignment.Top;
        }
        var area = new Grid();
        area.Children.Add(input);
        if (multiline)
        {
            FormattedPaste.Enable(input);
            var previewButton = new Button { Content = "预览", MinHeight = 26, Padding = new Thickness(8, 2, 8, 2) };
            previewButton.SetResourceReference(StyleProperty, "TextAction");
            DockPanel.SetDock(previewButton, Dock.Right);
            fieldBar.Children.Add(previewButton);
            var preview = new ScrollViewer
            {
                Visibility = Visibility.Collapsed, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            area.Children.Add(preview);
            MarkdownPreview.BindToggle(previewButton, input, preview);
        }
        heading.Margin = new Thickness(0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        fieldBar.Children.Add(heading);
        fields.Children.Add(area);
        Grid.SetRow(fields, 1);
        root.Children.Add(fields);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 13, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "QuietButton");
        var save = new Button { IsDefault = !multiline, IsEnabled = false, MinWidth = 80 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Content = new TextBlock { Text = "保存", Foreground = Brushes.White };
        save.Click += delegate { DialogResult = true; };
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        input.TextChanged += delegate { save.IsEnabled = !String.IsNullOrWhiteSpace(input.Text); };
        input.Text = initialText ?? "";
        Loaded += delegate
        {
            Keyboard.Focus(input);
            input.CaretIndex = input.Text.Length;
        };
        PreviewKeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && save.IsEnabled)
            {
                e.Handled = true;
                DialogResult = true;
            }
        };
    }

    internal static string Prompt(Window owner, string title, string label, int limit, bool multiline = false, string initialText = null)
    {
        var window = new TextEntryWindow(owner, title, label, limit, multiline, initialText);
        return window.ShowDialog() == true ? window.input.Text.Trim() : null;
    }
}
