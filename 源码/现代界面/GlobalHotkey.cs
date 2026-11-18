using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

internal sealed class GlobalHotkey : IDisposable
{
    internal const int Message = 0x0312;
    private const int FirstId = 814;
    private const int SecondId = 815;
    private const uint NoRepeat = 0x4000;
    private readonly IntPtr window;
    private int activeId;

    internal HotkeyGesture Current { get; private set; }

    internal GlobalHotkey(IntPtr window)
    {
        this.window = window;
    }

    internal void RegisterInitial(HotkeyGesture preferred)
    {
        var alternatives = new[]
        {
            preferred,
            HotkeyGesture.Default,
            new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.K),
            new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Shift, Key.F11)
        };
        foreach (var option in alternatives)
            if (option != null && TryChange(option, null))
                return;
    }

    internal bool TryChange(HotkeyGesture next, Action persist)
    {
        if (next == null || !next.IsValid)
            return false;
        if (next.Matches(Current))
        {
            if (persist != null)
                persist();
            return true;
        }

        int newId = activeId == FirstId ? SecondId : FirstId;
        if (!RegisterHotKey(window, newId, (uint)next.Modifiers | NoRepeat, (uint)KeyInterop.VirtualKeyFromKey(next.Key)))
            return false;
        try
        {
            if (persist != null)
                persist();
        }
        catch
        {
            UnregisterHotKey(window, newId);
            throw;
        }
        if (activeId != 0)
            UnregisterHotKey(window, activeId);
        activeId = newId;
        Current = next;
        return true;
    }

    internal bool IsActive(IntPtr messageId)
    {
        return activeId != 0 && messageId.ToInt64() == activeId;
    }

    public void Dispose()
    {
        if (activeId != 0)
            UnregisterHotKey(window, activeId);
        activeId = 0;
        Current = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
