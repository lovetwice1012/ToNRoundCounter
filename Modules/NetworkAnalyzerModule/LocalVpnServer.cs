using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    internal sealed class LocalVpnServer : IDisposable
    {
        private readonly int _proxyPort;
        private readonly int _vpnPort;
        private readonly IEventLogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private bool _disposed;

        public LocalVpnServer(int proxyPort, int vpnPort, IEventLogger logger)
        {
            if (proxyPort <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(proxyPort));
            }

            if (vpnPort <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vpnPort));
            }

            _proxyPort = proxyPort;
            _vpnPort = vpnPort;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalVpnServer));
            }

            if (_cts != null)
            {
                throw new InvalidOperationException("Server already started.");
            }

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Loopback, _vpnPort);
            listener.Start();
            _logger.LogEvent("NetworkAnalyzer.Vpn", $"Local VPN server listening on 127.0.0.1:{_vpnPort}.");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient? client = null;
                    try
                    {
                        var acceptTask = listener.AcceptTcpClientAsync();
                        var completed = await Task.WhenAny(acceptTask, Task.Delay(200, token)).ConfigureAwait(false);
                        if (completed != acceptTask)
                        {
                            if (completed.IsCanceled || token.IsCancellationRequested)
                            {
                                break;
                            }

                            continue;
                        }

                        client = await acceptTask.ConfigureAwait(false);
                        ToNRoundCounter.Infrastructure.AsyncErrorHandler.Execute(async () => await HandleClientAsync(client, token), "Handle VPN client connection");
                    }
                    catch (OperationCanceledException)
                    {
                        client?.Close();
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        client?.Close();
                        break;
                    }
                    catch (Exception ex)
                    {
                        client?.Close();
                        _logger.LogEvent("NetworkAnalyzer.Vpn", $"VPN listener error: {ex}", LogEventLevel.Error);
                    }
                }
            }
            finally
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("NetworkAnalyzer.Vpn", $"VPN listener stop failed: {ex.Message}", LogEventLevel.Debug);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var upstream = new TcpClient())
            {
                try
                {
                    await upstream.ConnectAsync(IPAddress.Loopback, _proxyPort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("NetworkAnalyzer.Vpn", $"Failed to connect VPN session to proxy: {ex}", LogEventLevel.Error);
                    return;
                }

                using (upstream)
                using (var clientStream = client.GetStream())
                using (var upstreamStream = upstream.GetStream())
                {
                    var upload = RelayAsync(clientStream, upstreamStream, token);
                    var download = RelayAsync(upstreamStream, clientStream, token);
                    await Task.WhenAny(upload, download).ConfigureAwait(false);
                }
            }
        }

        private static async Task RelayAsync(Stream input, Stream output, CancellationToken token)
        {
            var buffer = new byte[ToNRoundCounter.Infrastructure.Constants.Network.VpnBufferSize];
            while (!token.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("NetworkAnalyzer.Vpn", $"VPN CancellationTokenSource cancel failed: {ex.Message}", LogEventLevel.Debug);
                }

                _cts.Dispose();
                _cts = null;
            }

            if (_listenerTask != null)
            {
                try
                {
                    _listenerTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    _logger.LogEvent("NetworkAnalyzer.Vpn", $"VPN listener task wait failed: {ex.Message}", LogEventLevel.Debug);
                }

                _listenerTask = null;
            }
        }
    }
}
