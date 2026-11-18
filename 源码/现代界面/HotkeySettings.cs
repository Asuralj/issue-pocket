using System;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Input;

[DataContract]
internal sealed class HotkeyGesture
{
    internal static readonly HotkeyGesture Default = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.J);
    private const ModifierKeys SupportedModifiers = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows;

    [DataMember] internal ModifierKeys Modifiers { get; private set; }
    [DataMember] internal Key Key { get; private set; }

    internal HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    internal bool IsValid
    {
        get { return CanCapture && Key != Key.F12; }
    }

    internal bool CanCapture
    {
        get
        {
            return (Modifiers & ~SupportedModifiers) == ModifierKeys.None &&
                !IsModifierKey(Key) && KeyInterop.VirtualKeyFromKey(Key) != 0;
        }
    }

    internal static bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
    }

    internal bool Matches(HotkeyGesture other)
    {
        return other != null && Modifiers == other.Modifiers && Key == other.Key;
    }

    public override string ToString()
    {
        string name = Key.ToString();
        if (Key >= Key.D0 && Key <= Key.D9)
            name = ((int)(Key - Key.D0)).ToString();
        else if (Key == Key.Return)
            name = "Enter";
        else if (Key == Key.PageUp)
            name = "PageUp";
        else if (Key == Key.PageDown)
            name = "PageDown";
        else if (Key == Key.Back)
            name = "Backspace";
        else if (Key == Key.Escape)
            name = "Esc";
        return Prefix(ModifierKeys.Control, "Ctrl") + Prefix(ModifierKeys.Alt, "Alt") +
            Prefix(ModifierKeys.Shift, "Shift") + Prefix(ModifierKeys.Windows, "Win") + name;
    }

    private string Prefix(ModifierKeys flag, string name)
    {
        return (Modifiers & flag) != 0 ? name + "+" : "";
    }
}

internal sealed class HotkeySettings
{
    private const int MaxBytes = 4096;
    private readonly string path;

    internal HotkeySettings(string root)
    {
        path = Path.Combine(root, "hotkey.json");
    }

    internal HotkeyGesture Load()
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > MaxBytes)
                    return null;
                var gesture = LocalJson.Read<HotkeyGesture>(stream);
                return gesture != null && gesture.IsValid ? gesture : null;
            }
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException ||
                error is SerializationException || error is System.Xml.XmlException))
                throw;
            return null;
        }
    }

    internal void Save(HotkeyGesture gesture)
    {
        LocalJson.Save(path, gesture, MaxBytes, "快捷键设置过大。");
    }
}
