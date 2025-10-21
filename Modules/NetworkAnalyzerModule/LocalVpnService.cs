using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    internal sealed class LocalVpnService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly IEventLogger _logger;
        private LocalVpnServer? _server;
        private WindowsProxyConfigurator? _proxyConfigurator;
        private bool _disposed;
        private bool _isActive;
        private int _vpnPort;
        private int _proxyPort;

        public LocalVpnService(IEventLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsActive
        {
            get
            {
                lock (_sync)
                {
                    return _isActive;
                }
            }
        }

        public int CurrentVpnPort
        {
            get
            {
                lock (_sync)
                {
                    return _vpnPort;
                }
            }
        }

        public Task<bool> EnsureStartedAsync(int proxyPort)
        {
            return Task.Run(() =>
            {
                lock (_sync)
                {
                    ThrowIfDisposed();

                    if (_isActive && _proxyPort == proxyPort)
                    {
                        return true;
                    }

                    StopInternal();

                    _proxyPort = proxyPort;

                    int vpnPort = GetAvailablePort();
                    _server = new LocalVpnServer(proxyPort, vpnPort, _logger);
                    try
                    {
                        _server.Start();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("NetworkAnalyzer.Vpn", $"Failed to start local VPN server: {ex}", LogEventLevel.Error);
                        StopInternal();
                        return false;
                    }

                    _proxyConfigurator = new WindowsProxyConfigurator(_logger);
                    if (!_proxyConfigurator.Apply(vpnPort))
                    {
                        StopInternal();
                        return false;
                    }

                    _vpnPort = vpnPort;
                    _isActive = true;
                    _logger.LogEvent("NetworkAnalyzer.Vpn", $"Local VPN service is active on 127.0.0.1:{vpnPort}.");
                    return true;
                }
            });
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _proxyConfigurator?.Restore();
            _proxyConfigurator = null;

            if (_server != null)
            {
                try
                {
                    _server.Dispose();
                }
                catch
                {
                    // Ignore disposal exceptions.
                }
                _server = null;
            }

            if (_isActive)
            {
                _logger.LogEvent("NetworkAnalyzer.Vpn", "Local VPN service stopped.", LogEventLevel.Information);
            }

            _vpnPort = 0;
            _proxyPort = 0;
            _isActive = false;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalVpnService));
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                StopInternal();
                _disposed = true;
            }
        }
    }
}
