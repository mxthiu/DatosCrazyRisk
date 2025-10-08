#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrazyRiskGame.Net.LAN
{
    /// <summary>
    /// Implementación de lobby LAN:
    /// - Host: escucha TCP (clientes) y envía beacons UDP periódicos.
    /// - Client: descubre con UDP y conecta por TCP.
    /// - Mensajería simple de texto (línea por mensaje).
    /// </summary>
    public sealed class LanLobbyService : ILanLobbyService
    {
        // === constantes/protocolo ===
        private const int UDP_PORT = 47815;       // puerto fijo de beacons
        private const int UDP_INTERVAL_MS = 1000; // cada cuánto envía el host el beacon
        private const string MAGIC = "CRAZYRISK/BEACON/v1";
        private static readonly byte[] MAGIC_BYTES = Encoding.ASCII.GetBytes(MAGIC);

        // === estado ===
        public int HostPort { get; private set; } = 0;
        public string LobbyName { get; set; } = "CrazyRisk Lobby";
        public bool IsHosting => _listener != null;
        public bool IsClientConnected => _client?.Connected == true;

        public event Action<LobbyEvent>? OnLobbyEvent;

        private TcpListener? _listener;
        private readonly ConcurrentDictionary<IPEndPoint, TcpClient> _clients = new();
        private CancellationTokenSource? _hostCts;
        private Task? _hostAcceptLoop;
        private Task? _hostBeaconLoop;

        private TcpClient? _client;
        private Task? _clientReadLoop;
        private CancellationTokenSource? _clientCts;

        public void Dispose()
        {
            _ = StopHostAsync();
            _ = DisconnectAsync();
        }

        // === HOST ===
        public async Task StartHostAsync(int tcpPort, CancellationToken ct = default)
        {
            await StopHostAsync();

            try
            {
                _listener = new TcpListener(IPAddress.Any, tcpPort);
                _listener.Start();
                HostPort = tcpPort;

                _hostCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = _hostCts.Token;

                // loop de aceptar clientes
                _hostAcceptLoop = Task.Run(async () =>
                {
                    Raise(LobbyEvent.HostStarted(tcpPort));
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var tcp = await _listener.AcceptTcpClientAsync(token);
                            var ep = (IPEndPoint)tcp.Client.RemoteEndPoint!;
                            _clients[ep] = tcp;
                            Raise(LobbyEvent.ClientConnected(ep));
                            _ = HandleClientAsync(tcp, isServerSide: true, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Raise(LobbyEvent.Error("Accept failed: " + ex.Message));
                            await Task.Delay(250, token);
                        }
                    }
                }, token);

                // loop de beacons (UDP broadcast)
                _hostBeaconLoop = Task.Run(async () =>
                {
                    using var udp = new UdpClient();
                    udp.EnableBroadcast = true;
                    var bc = new IPEndPoint(IPAddress.Broadcast, UDP_PORT);

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var payload = BuildBeacon(LobbyName, HostPort);
                            await udp.SendAsync(payload, payload.Length, bc);
                            await Task.Delay(UDP_INTERVAL_MS, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Raise(LobbyEvent.Error("Beacon failed: " + ex.Message));
                            await Task.Delay(1000, token);
                        }
                    }
                }, token);
            }
            catch
            {
                await StopHostAsync();
                throw;
            }
        }

        public async Task StopHostAsync()
        {
            try { _hostCts?.Cancel(); } catch { }
            _hostCts = null;

            var clients = _clients.ToArray();
            _clients.Clear();
            foreach (var kv in clients)
            {
                try { kv.Value.Close(); kv.Value.Dispose(); } catch { }
            }

            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }

            var tasks = new[] { _hostAcceptLoop, _hostBeaconLoop }.Where(t => t != null)!.Cast<Task>().ToArray();
            _hostAcceptLoop = _hostBeaconLoop = null;
            if (tasks.Length > 0)
            {
                try { await Task.WhenAll(tasks); } catch { }
            }

            if (HostPort != 0) Raise(LobbyEvent.HostStopped());
            HostPort = 0;
        }

        // === DISCOVERY (CLIENTE) ===
        public async Task DiscoverAsync(int timeoutMs, Action<LobbyBeacon>? onFound, CancellationToken ct = default)
        {
            using var udp = new UdpClient(UDP_PORT)
            {
                EnableBroadcast = true
            };
            udp.Client.ReceiveTimeout = timeoutMs;

            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(ct);
                    if (TryParseBeacon(result.Buffer, result.RemoteEndPoint.Address, out var bk))
                    {
                        onFound?.Invoke(bk);
                        Raise(LobbyEvent.Beacon(bk));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { /* timeout está bien */ break; }
                catch (Exception ex)
                {
                    Raise(LobbyEvent.Error("Discover error: " + ex.Message));
                    break;
                }
            }
        }

        // === CLIENTE TCP ===
        public async Task ConnectAsync(IPAddress host, int port, CancellationToken ct = default)
        {
            await DisconnectAsync();

            _clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _clientCts.Token;

            _client = new TcpClient();
            await _client.ConnectAsync(host, port, token);
            Raise(LobbyEvent.Info($"Connected to {host}:{port}"));

            _clientReadLoop = Task.Run(() => HandleClientAsync(_client!, isServerSide: false, token), token);
        }

        public async Task DisconnectAsync()
        {
            try { _clientCts?.Cancel(); } catch { }
            _clientCts = null;

            if (_client != null)
            {
                try { _client.Close(); _client.Dispose(); } catch { }
                _client = null;
            }

            var t = _clientReadLoop;
            _clientReadLoop = null;
            if (t != null) { try { await t; } catch { } }
        }

        // === MENSAJERÍA ===
        public async Task SendAsync(string message, CancellationToken ct = default)
        {
            var data = Encoding.UTF8.GetBytes(message + "\n");

            if (IsHosting)
            {
                // broadcast a todos los clientes
                foreach (var kv in _clients.ToArray())
                {
                    try
                    {
                        var ns = kv.Value.GetStream();
                        await ns.WriteAsync(data, 0, data.Length, ct);
                    }
                    catch
                    {
                        // si falla, ciérralo
                        if (_clients.TryRemove(kv.Key, out var c))
                        {
                            try { c.Close(); c.Dispose(); } catch { }
                            Raise(LobbyEvent.ClientDisconnected(kv.Key));
                        }
                    }
                }
            }
            else if (_client != null && _client.Connected)
            {
                var ns = _client.GetStream();
                await ns.WriteAsync(data, 0, data.Length, ct);
            }
            else
            {
                Raise(LobbyEvent.Error("No hay socket activo para enviar."));
            }
        }

        // === internos ===
        private async Task HandleClientAsync(TcpClient tcp, bool isServerSide, CancellationToken ct)
        {
            var ep = (IPEndPoint?)tcp.Client.RemoteEndPoint;
            using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    Raise(LobbyEvent.Msg(ep, line));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Raise(LobbyEvent.Error($"Socket error {ep}: {ex.Message}"));
            }
            finally
            {
                if (isServerSide && ep != null)
                {
                    if (_clients.TryRemove(ep, out var c))
                    {
                        try { c.Close(); c.Dispose(); } catch { }
                        Raise(LobbyEvent.ClientDisconnected(ep));
                    }
                }
                else if (!isServerSide)
                {
                    Raise(LobbyEvent.Info("Disconnected from host"));
                }
            }
        }

        private static byte[] BuildBeacon(string name, int port)
        {
            // [MAGIC][nameLen(ushort)][name(utf8)][port(ushort)]
            var nameBytes = Encoding.UTF8.GetBytes(name);
            using var ms = new MemoryStream();
            ms.Write(MAGIC_BYTES, 0, MAGIC_BYTES.Length);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)nameBytes.Length);
            ms.Write(buf);
            ms.Write(nameBytes, 0, nameBytes.Length);
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)port);
            ms.Write(buf);
            return ms.ToArray();
        }

        private static bool TryParseBeacon(ReadOnlySpan<byte> payload, IPAddress sender, out LobbyBeacon beacon)
        {
            beacon = default!;
            if (payload.Length < MAGIC_BYTES.Length + 2 + 2) return false;
            if (!payload.Slice(0, MAGIC_BYTES.Length).SequenceEqual(MAGIC_BYTES)) return false;

            var span = payload.Slice(MAGIC_BYTES.Length);
            ushort nameLen = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(0, 2));
            span = span.Slice(2);
            if (span.Length < nameLen + 2) return false;

            var name = Encoding.UTF8.GetString(span.Slice(0, nameLen));
            span = span.Slice(nameLen);
            ushort port = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(0, 2));

            beacon = new LobbyBeacon
            {
                Name = name,
                Address = sender,
                Port = port
            };
            return true;
        }

        private void Raise(LobbyEvent ev) => OnLobbyEvent?.Invoke(ev);
    }
}