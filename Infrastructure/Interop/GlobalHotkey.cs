using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ToNRoundCounter.Infrastructure.Interop
{
    /// <summary>
    /// Process-wide global hotkey manager.
    /// Wraps Win32 RegisterHotKey via an internal NativeWindow that listens for WM_HOTKEY,
    /// so callers do not need to override their own WndProc.
    /// </summary>
    public sealed class GlobalHotkey : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        [Flags]
        public enum HotkeyModifiers : uint
        {
            None = 0x0000,
            Alt = 0x0001,
            Control = 0x0002,
            Shift = 0x0004,
            Win = 0x0008,
            NoRepeat = 0x4000,
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private sealed class HotkeyWindow : NativeWindow
        {
            public event EventHandler<int>? HotkeyPressed;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    HotkeyPressed?.Invoke(this, m.WParam.ToInt32());
                }
                base.WndProc(ref m);
            }
        }

        private readonly HotkeyWindow _window;
        private readonly Dictionary<int, Action> _callbacks = new Dictionary<int, Action>();
        private int _nextId = 1;
        private bool _disposed;

        public GlobalHotkey()
        {
            _window = new HotkeyWindow();
            _window.HotkeyPressed += OnHotkeyPressed;
        }

        /// <summary>
        /// Registers a hotkey with the given modifiers/key. Returns the registration id, or -1 on failure.
        /// </summary>
        public int Register(HotkeyModifiers modifiers, Keys key, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (key == Keys.None) return -1;
            int id = _nextId++;
            uint vk = (uint)(key & Keys.KeyCode);
            if (!RegisterHotKey(_window.Handle, id, (uint)(modifiers | HotkeyModifiers.NoRepeat), vk))
            {
                return -1;
            }
            _callbacks[id] = callback;
            return id;
        }

        public void Unregister(int id)
        {
            if (id <= 0) return;
            if (_callbacks.Remove(id))
            {
                try { UnregisterHotKey(_window.Handle, id); } catch { /* ignore */ }
            }
        }

        public void UnregisterAll()
        {
            foreach (var id in new List<int>(_callbacks.Keys))
            {
                try { UnregisterHotKey(_window.Handle, id); } catch { /* ignore */ }
            }
            _callbacks.Clear();
        }

        private void OnHotkeyPressed(object? sender, int id)
        {
            if (_callbacks.TryGetValue(id, out var cb))
            {
                try { cb(); } catch { /* swallow to avoid crashing message loop */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterAll();
            try { _window.DestroyHandle(); } catch { /* ignore */ }
        }

        /// <summary>
        /// Parses a hotkey string like "Ctrl+Shift+M" into modifiers and a key.
        /// Returns false for empty/invalid strings.
        /// </summary>
        public static bool TryParse(string? text, out HotkeyModifiers modifiers, out Keys key)
        {
            modifiers = HotkeyModifiers.None;
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            foreach (string raw in parts)
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;
                switch (token.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= HotkeyModifiers.Control; break;
                    case "shift":
                        modifiers |= HotkeyModifiers.Shift; break;
                    case "alt":
                        modifiers |= HotkeyModifiers.Alt; break;
                    case "win":
                    case "windows":
                        modifiers |= HotkeyModifiers.Win; break;
                    default:
                        if (Enum.TryParse<Keys>(token, true, out var parsed))
                        {
                            key = parsed & Keys.KeyCode;
                        }
                        else
                        {
                            return false;
                        }
                        break;
                }
            }
            return key != Keys.None;
        }

        /// <summary>
        /// Formats modifier/key into the canonical "Ctrl+Shift+M" string.
        /// </summary>
        public static string Format(Keys keyData)
        {
            Keys mods = keyData & Keys.Modifiers;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.None || key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            if ((mods & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((mods & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            if ((mods & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }
    }
}
