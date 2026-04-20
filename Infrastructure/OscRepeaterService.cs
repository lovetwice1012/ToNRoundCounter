using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Rug.Osc;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// In-process OSC repeater. Listens on a single UDP port (e.g. 9001) and
    /// forwards every received packet to one or more destination endpoints,
    /// replacing the external OscRepeater.exe process.
    /// </summary>
    public sealed class OscRepeaterService : IOscRepeaterService
    {
        private readonly IEventLogger _logger;
        private readonly ICancellationProvider _cancellation;
        private readonly ConcurrentBag<(string Host, int Port)> _destinations = new();

        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly object _lock = new();

        public OscRepeaterService(IEventLogger logger, ICancellationProvider cancellation)
        {
            _logger = logger;
            _cancellation = cancellation;
        }

        public void AddDestination(string host, int port)
        {
            _destinations.Add((host, port));
            _logger.LogEvent("OscRepeater", $"Forwarding destination added: {host}:{port}.");
        }

        public Task StartAsync(int sourcePort)
        {
            lock (_lock)
            {
                if (_listenTask != null && !_listenTask.IsCompleted)
                {
                    _logger.LogEvent("OscRepeater", "Start requested while already running.", Serilog.Events.LogEventLevel.Debug);
                    return _listenTask;
                }

                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                var token = _cts.Token;
                _listenTask = Task.Run(() => RunAsync(sourcePort, token), token);
                return Task.CompletedTask;
            }
        }

        private void RunAsync(int sourcePort, CancellationToken token)
        {
            OscReceiver? receiver = null;
            UdpClient? sender = null;
            int forwarded = 0;

            try
            {
                _logger.LogEvent("OscRepeater", $"Starting repeater on port {sourcePort}.");
                receiver = new OscReceiver(IPAddress.Parse("127.0.0.1"), sourcePort);
                receiver.Connect();
                sender = new UdpClient();
                _logger.LogEvent("OscRepeater", $"Repeater listening on 127.0.0.1:{sourcePort}.");

                while (!token.IsCancellationRequested)
                {
                    if (receiver.State != OscSocketState.Connected)
                    {
                        _logger.LogEvent("OscRepeater", "Receiver disconnected. Stopping repeater.");
                        break;
                    }

                    if (receiver.TryReceive(out OscPacket packet))
                    {
                        byte[] data = packet.ToByteArray();
                        foreach (var (host, port) in _destinations)
                        {
                            try
                            {
                                sender.Send(data, data.Length, host, port);
                            }
                            catch (SocketException ex)
                            {
                                _logger.LogEvent("OscRepeater",
                                    $"Failed to forward to {host}:{port}: {ex.Message}",
                                    Serilog.Events.LogEventLevel.Warning);
                            }
                        }

                        forwarded++;
                        if (forwarded <= 3 || forwarded % 500 == 0)
                        {
                            _logger.LogEvent("OscRepeater",
                                () => $"Forwarded {forwarded} packet(s).",
                                Serilog.Events.LogEventLevel.Debug);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("OscRepeater", $"Repeater error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                sender?.Dispose();
                try { receiver?.Dispose(); } catch { }
                _logger.LogEvent("OscRepeater", $"Repeater stopped after forwarding {forwarded} packet(s).");
            }
        }

        public void Stop()
        {
            _logger.LogEvent("OscRepeater", "Stop requested.");

            Task? task;
            CancellationTokenSource? cts;

            lock (_lock)
            {
                cts = _cts;
                task = _listenTask;
                _cts = null;
                _listenTask = null;
            }

            if (cts == null) return;

            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }

            try
            {
                task?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogEvent("OscRepeater", $"Error during stop: {ex.Message}", Serilog.Events.LogEventLevel.Warning);
            }
            finally
            {
                cts.Dispose();
            }

            _logger.LogEvent("OscRepeater", "Repeater stopped.");
        }

        public void Dispose()
        {
            try { Stop(); }
            catch { }
        }
    }
}
