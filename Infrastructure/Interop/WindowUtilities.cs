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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
