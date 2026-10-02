using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace PeakCraft.Input;

/// <summary>
/// Unity Input System keys to SDL scancodes (USB HID usage ids), which is what Minecraft 26.x reads.
/// </summary>
internal static class Scancodes
{
    public const ushort Escape = 41;

    public static readonly Dictionary<Key, ushort> FromKey = Build();

    private static Dictionary<Key, ushort> Build()
    {
        var map = new Dictionary<Key, ushort>();
        for (int i = 0; i < 26; i++)
        {
            map[Key.A + i] = (ushort)(4 + i); // a..z = 4..29
        }
        for (int i = 0; i < 9; i++)
        {
            map[Key.Digit1 + i] = (ushort)(30 + i); // 1..9 = 30..38
        }
        map[Key.Digit0] = 39;
        map[Key.Enter] = 40;
        map[Key.Escape] = Escape;
        map[Key.Backspace] = 42;
        map[Key.Tab] = 43;
        map[Key.Space] = 44;
        map[Key.Minus] = 45;
        map[Key.Equals] = 46;
        map[Key.LeftBracket] = 47;
        map[Key.RightBracket] = 48;
        map[Key.Backslash] = 49;
        map[Key.Semicolon] = 51;
        map[Key.Quote] = 52;
        map[Key.Backquote] = 53;
        map[Key.Comma] = 54;
        map[Key.Period] = 55;
        map[Key.Slash] = 56;
        map[Key.CapsLock] = 57;
        for (int i = 0; i < 12; i++)
        {
            map[Key.F1 + i] = (ushort)(58 + i); // F1..F12 = 58..69
        }
        map[Key.PrintScreen] = 70;
        map[Key.ScrollLock] = 71;
        map[Key.Pause] = 72;
        map[Key.Insert] = 73;
        map[Key.Home] = 74;
        map[Key.PageUp] = 75;
        map[Key.Delete] = 76;
        map[Key.End] = 77;
        map[Key.PageDown] = 78;
        map[Key.RightArrow] = 79;
        map[Key.LeftArrow] = 80;
        map[Key.DownArrow] = 81;
        map[Key.UpArrow] = 82;
        map[Key.NumLock] = 83;
        map[Key.NumpadDivide] = 84;
        map[Key.NumpadMultiply] = 85;
        map[Key.NumpadMinus] = 86;
        map[Key.NumpadPlus] = 87;
        map[Key.NumpadEnter] = 88;
        for (int i = 0; i < 9; i++)
        {
            map[Key.Numpad1 + i] = (ushort)(89 + i); // keypad 1..9 = 89..97
        }
        map[Key.Numpad0] = 98;
        map[Key.NumpadPeriod] = 99;
        map[Key.OEM1] = 100; // non-US backslash
        map[Key.ContextMenu] = 101;
        map[Key.NumpadEquals] = 103;
        map[Key.LeftCtrl] = 224;
        map[Key.LeftShift] = 225;
        map[Key.LeftAlt] = 226;
        map[Key.LeftMeta] = 227;
        map[Key.RightCtrl] = 228;
        map[Key.RightShift] = 229;
        map[Key.RightAlt] = 230;
        map[Key.RightMeta] = 231;
        return map;
    }
}
