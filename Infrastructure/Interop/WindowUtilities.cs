using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ToNRoundCounter.Infrastructure.Interop
{
    public static class WindowUtilities
    {
        public static bool IsProcessInForeground(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                string? name = null;
                try
                {
                    name = process.MainModule?.ModuleName;
                }
                catch
                {
                    name = process.ProcessName;
                }

                if (string.IsNullOrEmpty(name))
                {
                    return false;
                }

                string normalizedProcess = System.IO.Path.GetFileNameWithoutExtension(name);
                return string.Equals(normalizedProcess, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryFocusProcessWindow(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            IntPtr handle = process.MainWindowHandle;
                            if (handle == IntPtr.Zero)
                            {
                                continue;
                            }

                            if (IsIconic(handle))
                            {
                                _ = ShowWindow(handle, SW_RESTORE);
                            }

                            if (SetForegroundWindow(handle))
                            {
                                return true;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool TryFocusProcessWindowIfAltNotPressed(string processName)
        {
            if (IsAltPressed())
            {
                return false;
            }

            return TryFocusProcessWindow(processName);
        }

        public static bool IsAltPressed()
        {
            return (GetAsyncKeyState(VK_MENU) & KeyPressedFlag) != 0;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int SW_RESTORE = 9;
        private const int VK_MENU = 0x12;
        private const int KeyPressedFlag = 0x8000;
    }
}
