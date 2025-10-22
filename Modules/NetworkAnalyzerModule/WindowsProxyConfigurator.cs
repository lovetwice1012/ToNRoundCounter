using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    internal sealed class WindowsProxyConfigurator
    {
        private const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";
        private readonly IEventLogger _logger;
        private ProxySettings? _backup;
        private bool _applied;

        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionRefresh = 37;

        public WindowsProxyConfigurator(IEventLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Apply(int vpnPort)
        {
            if (!IsWindows())
            {
                _logger.LogEvent("NetworkAnalyzer.Vpn", "Local VPN configuration is only supported on Windows.", LogEventLevel.Warning);
                return false;
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true))
                {
                    if (key == null)
                    {
                        _logger.LogEvent("NetworkAnalyzer.Vpn", "Failed to open Internet Settings registry key.", LogEventLevel.Error);
                        return false;
                    }

                    var rawEnable = key.GetValue("ProxyEnable");
                    _backup = new ProxySettings
                    {
                        ProxyEnable = rawEnable != null ? Convert.ToInt32(rawEnable) : 0,
                        ProxyServer = key.GetValue("ProxyServer") as string,
                        ProxyOverride = key.GetValue("ProxyOverride") as string
                    };

                    key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", $"127.0.0.1:{vpnPort}", RegistryValueKind.String);
                    key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
                }

                NotifyProxyChanged();
                _applied = true;
                _logger.LogEvent("NetworkAnalyzer.Vpn", $"System proxy redirected to local VPN endpoint 127.0.0.1:{vpnPort}.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer.Vpn", $"Failed to configure system proxy: {ex}", LogEventLevel.Error);
                try
                {
                    Restore();
                }
                catch
                {
                    // Ignore restoration failures while handling an error.
                }

                return false;
            }
        }

        public void Restore()
        {
            if (!IsWindows())
            {
                return;
            }

            if (_backup == null && !_applied)
            {
                return;
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (_backup != null)
                    {
                        key.SetValue("ProxyEnable", _backup.ProxyEnable, RegistryValueKind.DWord);

                        if (_backup.ProxyServer != null)
                        {
                            key.SetValue("ProxyServer", _backup.ProxyServer, RegistryValueKind.String);
                        }
                        else
                        {
                            key.DeleteValue("ProxyServer", false);
                        }

                        if (_backup.ProxyOverride != null)
                        {
                            key.SetValue("ProxyOverride", _backup.ProxyOverride, RegistryValueKind.String);
                        }
                        else
                        {
                            key.DeleteValue("ProxyOverride", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer.Vpn", $"Failed to restore system proxy configuration: {ex}", LogEventLevel.Error);
            }
            finally
            {
                NotifyProxyChanged();
                _applied = false;
                _backup = null;
                _logger.LogEvent("NetworkAnalyzer.Vpn", "System proxy configuration restored.", LogEventLevel.Information);
            }
        }

        private static bool IsWindows()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                case PlatformID.WinCE:
                    return true;
                default:
                    return false;
            }
        }

        private static void NotifyProxyChanged()
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private sealed class ProxySettings
        {
            public int ProxyEnable { get; set; }

            public string? ProxyServer { get; set; }

            public string? ProxyOverride { get; set; }
        }
    }
}
