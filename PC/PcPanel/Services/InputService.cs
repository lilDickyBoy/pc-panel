using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>Touchpad + keyboard from the phone, via SendInput.</summary>
    public static class InputService
    {
        static readonly int Size = Marshal.SizeOf<Native.INPUT>();

        static void Send(params Native.INPUT[] inputs)
        {
            if (inputs.Length == 0) return;
            Native.SendInput((uint)inputs.Length, inputs, Size);
        }

        static Native.INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) => new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags } }
        };

        static Native.INPUT Key(ushort vk, bool up, bool extended = false) => new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0) } }
        };

        static Native.INPUT Unicode(char c, bool up) => new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0) } }
        };

        public static void Move(int dx, int dy) => Send(Mouse(Native.MOUSEEVENTF_MOVE, dx, dy));

        public static void Click(string button, bool dbl)
        {
            var (down, up) = Flags(button);
            if (dbl) Send(Mouse(down), Mouse(up), Mouse(down), Mouse(up));
            else Send(Mouse(down), Mouse(up));
        }

        public static void Button(string button, bool isDown)
        {
            var (down, up) = Flags(button);
            Send(Mouse(isDown ? down : up));
        }

        static (uint, uint) Flags(string b) => b switch
        {
            "right" => (Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_RIGHTUP),
            "middle" => (Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_MIDDLEUP),
            _ => (Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP)
        };

        public static void Scroll(int dy, int dx)
        {
            if (dy != 0) Send(Mouse(Native.MOUSEEVENTF_WHEEL, data: dy));
            if (dx != 0) Send(Mouse(Native.MOUSEEVENTF_HWHEEL, data: dx));
        }

        public static void Text(string text)
        {
            var list = new List<Native.INPUT>();
            foreach (var c in text)
            {
                if (c == '\n') { list.Add(Key(0x0D, false)); list.Add(Key(0x0D, true)); continue; }
                if (c == '\r') continue;
                list.Add(Unicode(c, false));
                list.Add(Unicode(c, true));
            }
            Send(list.ToArray());
        }

        static readonly Dictionary<string, (ushort vk, bool ext)> Keys = new Dictionary<string, (ushort, bool)>
        {
            ["enter"] = (0x0D, false), ["backspace"] = (0x08, false), ["tab"] = (0x09, false), ["esc"] = (0x1B, false),
            ["space"] = (0x20, false), ["left"] = (0x25, true), ["up"] = (0x26, true), ["right"] = (0x27, true), ["down"] = (0x28, true),
            ["delete"] = (0x2E, true), ["insert"] = (0x2D, true), ["home"] = (0x24, true), ["end"] = (0x23, true), ["pgup"] = (0x21, true), ["pgdn"] = (0x22, true),
            ["win"] = (0x5B, true), ["ctrl"] = (0x11, false), ["alt"] = (0x12, false), ["shift"] = (0x10, false),
            ["printscreen"] = (0x2C, true), ["capslock"] = (0x14, false),
            ["volup"] = (0xAF, true), ["voldown"] = (0xAE, true), ["volmute"] = (0xAD, true),
            ["next"] = (0xB0, true), ["prev"] = (0xB1, true), ["playpause"] = (0xB3, true),
        };

        public static void Combo(IList<string> names)
        {
            var vks = new List<(ushort, bool)>();
            foreach (var raw in names)
            {
                var n = raw.ToLowerInvariant();
                if (Keys.TryGetValue(n, out var k)) vks.Add(k);
                else if (n.Length >= 2 && n[0] == 'f' && int.TryParse(n.Substring(1), out int f) && f >= 1 && f <= 12) vks.Add(((ushort)(0x70 + f - 1), false));
                else if (n.Length == 1 && char.IsLetterOrDigit(n[0])) vks.Add(((ushort)char.ToUpperInvariant(n[0]), false));
            }
            if (vks.Count == 0) return;
            var list = new List<Native.INPUT>();
            foreach (var (vk, ext) in vks) list.Add(Key(vk, false, ext));
            for (int i = vks.Count - 1; i >= 0; i--) list.Add(Key(vks[i].Item1, true, vks[i].Item2));
            Send(list.ToArray());
        }
    }
}
