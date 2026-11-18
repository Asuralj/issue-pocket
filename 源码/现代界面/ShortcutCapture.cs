using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

internal sealed class ShortcutCapture
{
    private const string Instructions = "点击键位按钮后直接按键，支持单键和组合键。";
    private readonly Button field;
    private readonly Button apply;
    private readonly TextBlock feedback;
    private bool recording;
    private Key consumedKey;

    internal HotkeyGesture Value { get; private set; }

    internal ShortcutCapture(Button field, Button apply, TextBlock feedback, HotkeyGesture initial)
    {
        this.field = field;
        this.apply = apply;
        this.feedback = feedback;
        Value = initial;
        RestoreField();
        field.Click += delegate
        {
            if (recording)
                Cancel();
            else
                Begin();
        };
        field.LostKeyboardFocus += delegate
        {
            consumedKey = Key.None;
            if (recording)
                Cancel();
        };
        field.PreviewKeyDown += OnKeyDown;
        field.PreviewKeyUp += OnKeyUp;
    }

    private void Begin()
    {
        Keyboard.Focus(field);
        recording = true;
        consumedKey = Key.None;
        field.Content = "请按快捷键…";
        apply.IsEnabled = false;
        feedback.Text = "按一个键，或按住 Ctrl、Alt、Shift、Win 后再按主键。";
    }

    private void Cancel()
    {
        recording = false;
        RestoreField();
        feedback.Text = Instructions;
    }

    internal bool Record(HotkeyGesture gesture)
    {
        if (!recording || gesture == null || HotkeyGesture.IsModifierKey(gesture.Key))
            return false;
        if (!gesture.CanCapture)
        {
            feedback.Text = "无法识别这个键，请按另一个键。";
            return false;
        }
        recording = false;
        Value = gesture;
        RestoreField();
        feedback.Text = gesture.Modifiers == ModifierKeys.None
            ? "已录入单键。应用后，在其他软件中按这个键也会触发。"
            : "已录入组合键，点击「应用」生效。";
        return true;
    }

    private void RestoreField()
    {
        field.Content = Value == null ? "点击录入" : Value.ToString();
        apply.IsEnabled = Value != null;
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        Key key = EventKey(args);
        if (consumedKey != Key.None && key == consumedKey && args.IsRepeat)
        {
            args.Handled = true;
            return;
        }
        if (!recording)
            return;
        args.Handled = true;
        if (!args.IsRepeat && Record(new HotkeyGesture(Keyboard.Modifiers, key)))
            consumedKey = key;
    }

    private void OnKeyUp(object sender, KeyEventArgs args)
    {
        if (consumedKey != Key.None && EventKey(args) == consumedKey)
        {
            consumedKey = Key.None;
            args.Handled = true;
        }
        else if (recording)
            args.Handled = true;
    }

    private static Key EventKey(KeyEventArgs args)
    {
        if (args.Key == Key.System)
            return args.SystemKey;
        if (args.Key == Key.ImeProcessed)
            return args.ImeProcessedKey;
        if (args.Key == Key.DeadCharProcessed)
            return args.DeadCharProcessedKey;
        return args.Key;
    }
}
