using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HandyBadgeRemote
{
    // Sends a key press to whichever window has focus, using SendInput.
    static class KeySender
    {
        const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static bool TryParse(string name, out Keys key)
        {
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
            return Enum.TryParse(name.Trim(), true, out key);
        }

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                case Keys.Insert: case Keys.Delete:
                    return true;
                default: return false;
            }
        }

        // Returns an empty string on success, or a reason.
        public static string Send(string keyName)
        {
            Keys k;
            if (!TryParse(keyName, out k)) return "unknown key name '" + keyName + "'";
            if (k == Keys.None) return "";
            ushort vk = (ushort)(k & Keys.KeyCode);
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            uint ext = IsExtended(k) ? KEYEVENTF_EXTENDEDKEY : 0;
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = ext };
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = ext | KEYEVENTF_KEYUP };
            uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == 2 ? "" : "SendInput blocked (error " + Marshal.GetLastWin32Error() + ")";
        }
    }
}
