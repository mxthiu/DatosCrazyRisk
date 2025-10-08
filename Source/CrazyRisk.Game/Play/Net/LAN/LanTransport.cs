#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrazyRiskGame.Net.Lan
{
    /// <summary>
    /// Transporte TCP sencillo:
    /// - Host: StartServer(port) → acepta clientes.
    /// - Cliente: Connect(host, port).
    /// Protocolo: JSON por líneas (UTF8, '\n').
    /// </summary>
    public sealed class LanTransport : IDisposable
    {
        public const int DEFAULT_TCP_PORT = 47501;

        public sealed class ClientInfo
        {
            public Guid Id { get; } = Guid.NewGuid();
            public TcpClient Tcp { get; }
            public NetworkStream Stream => Tcp.GetStream();
            public string? Name { get; set; }
            public int? Avatar { get; set; }
            public bool Ready { get; set; }

            public ClientInfo(TcpClient tcp) { Tcp = tcp; }
            public override string ToString() => $"{Name ?? Id.ToString()}";
        }

        private TcpListener? _listener;
        private CancellationTokenSource? _ctsServer;
        private readonly ConcurrentDictionary<Guid, ClientInfo> _clients = new();

        private TcpClient? _client;
        private CancellationTokenSource? _ctsClient;

        public event Action<ClientInfo>? OnClientConnected;     // server
        public event Action<ClientInfo>? OnClientDisconnected;  // server
        public event Action<ClientInfo, string>? OnServerMessage; // server: (client,msg)

        public event Action<string>? OnClientMessage; // client: (msg)
        public event Action<string>? OnLog;

        // ======== SERVER ========
        public void StartServer(int port)
        {
            StopServer();

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _ctsServer = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                var ct = _ctsServer!.Token;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var tcp = await _listener.AcceptTcpClientAsync(ct);
                        tcp.NoDelay = true;
                        var ci = new ClientInfo(tcp);
                        _clients[ci.Id] = ci;
                        OnClientConnected?.Invoke(ci);
                        _ = HandleClientReadLoop(ci, isServerSide: true, ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { OnLog?.Invoke($"[Server] Accept error: {ex.Message}"); }
            }, _ctsServer.Token);
        }

        private async Task HandleClientReadLoop(ClientInfo ci, bool isServerSide, CancellationToken ct)
        {
            var stream = ci.Stream;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    if (isServerSide)
                        OnServerMessage?.Invoke(ci, line);
                    else
                        OnClientMessage?.Invoke(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { /* desconexión normal */ }
            catch (Exception ex) { OnLog?.Invoke($"[ReadLoop] {ex.Message}"); }
            finally
            {
                if (isServerSide)
                {
                    _clients.TryRemove(ci.Id, out _);
                    try { ci.Tcp.Close(); } catch { }
                    OnClientDisconnected?.Invoke(ci);
                }
                else
                {
                    try { _client?.Close(); } catch { }
                    _client = null;
                }
            }
        }

        public async Task ServerSendAsync(ClientInfo ci, object payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await ci.Stream.WriteAsync(bytes, 0, bytes.Length, ct);
        }

        public async Task ServerBroadcastAsync(object payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            foreach (var kv in _clients)
            {
                var s = kv.Value.Stream;
                try { await s.WriteAsync(bytes, 0, bytes.Length, ct); } catch { /* ignore */ }
            }
        }

        public void StopServer()
        {
            try { _ctsServer?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            foreach (var kv in _clients)
            {
                try { kv.Value.Tcp.Close(); } catch { }
            }
            _clients.Clear();
            _ctsServer = null; _listener = null;
        }

        // ======== CLIENT ========
        public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
        {
            Disconnect();

            try
            {
                _client = new TcpClient();
                _client.NoDelay = true;
                await _client.ConnectAsync(host, port, ct);
                _ctsClient = new CancellationTokenSource();
                _ = HandleClientReadLoop(new ClientInfo(_client), isServerSide: false, _ctsClient.Token);
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Client] Connect error: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        public async Task ClientSendAsync(object payload, CancellationToken ct = default)
        {
            if (_client == null) return;
            var stream = _client.GetStream();
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        }

        public void Disconnect()
        {
            try { _ctsClient?.Cancel(); } catch { }
            try { _client?.Close(); } catch { }
            _ctsClient = null; _client = null;
        }

        public void Dispose()
        {
            StopServer();
            Disconnect();
        }
    }
}