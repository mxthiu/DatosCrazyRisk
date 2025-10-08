#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrazyRiskGame.Net.Lan
{
    /// <summary>
    /// Descubrimiento LAN por UDP broadcast.
    /// - Host: llama StartHostBeacon(...) para anunciar el lobby cada ~1s.
    /// - Cliente: llama StartListen() para descubrir lobbies (evento OnLobbyAnnounced).
    /// </summary>
    public sealed class LanDiscovery : IDisposable
    {
        public const int DISCOVERY_PORT = 47500;
        private CancellationTokenSource? _ctsListen;
        private CancellationTokenSource? _ctsBeacon;
        private UdpClient? _udpListen;
        private UdpClient? _udpSend;

        public sealed class LobbyAnnounce
        {
            public string? Name { get; set; }
            public string? HostIp { get; set; }
            public int TcpPort { get; set; }
            public int Players { get; set; }
            public int MaxPlayers { get; set; }
            public DateTime SeenUtc { get; set; }
        }

        public event Action<LobbyAnnounce>? OnLobbyAnnounced;
        private readonly ConcurrentDictionary<string, LobbyAnnounce> _lastByKey = new();

        public void StartListen()
        {
            StopListen();

            _ctsListen = new CancellationTokenSource();
            _udpListen = new UdpClient(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));
            _udpListen.EnableBroadcast = true;

            _ = Task.Run(async () =>
            {
                var ct = _ctsListen!.Token;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var res = await _udpListen.ReceiveAsync(ct);
                        var json = Encoding.UTF8.GetString(res.Buffer);
                        var obj = JsonSerializer.Deserialize<LobbyAnnounce>(json);
                        if (obj != null)
                        {
                            obj.SeenUtc = DateTime.UtcNow;
                            // clave por ip:puerto
                            string key = $"{obj.HostIp}:{obj.TcpPort}";
                            _lastByKey[key] = obj;
                            OnLobbyAnnounced?.Invoke(obj);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { /* ignorar paquetes inválidos */ }
                }
            }, _ctsListen.Token);
        }

        public void StopListen()
        {
            try { _ctsListen?.Cancel(); } catch { }
            try { _udpListen?.Dispose(); } catch { }
            _ctsListen = null; _udpListen = null;
        }

        public void StartHostBeacon(string lobbyName, int tcpPort, int players, int maxPlayers)
        {
            StopHostBeacon();

            _ctsBeacon = new CancellationTokenSource();
            _udpSend = new UdpClient() { EnableBroadcast = true };

            // Determinar IP local visible (mejor esfuerzo)
            string hostIp = GetLocalIPv4() ?? "127.0.0.1";

            _ = Task.Run(async () =>
            {
                var ct = _ctsBeacon!.Token;
                var endp = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
                while (!ct.IsCancellationRequested)
                {
                    var announce = new LobbyAnnounce
                    {
                        Name = lobbyName,
                        HostIp = hostIp,
                        TcpPort = tcpPort,
                        Players = players,
                        MaxPlayers = maxPlayers,
                        SeenUtc = DateTime.UtcNow
                    };
                    var json = JsonSerializer.Serialize(announce);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    try { await _udpSend!.SendAsync(bytes, bytes.Length, endp); }
                    catch { /* ignorar */ }

                    await Task.Delay(1000, ct);
                }
            }, _ctsBeacon.Token);
        }

        public void StopHostBeacon()
        {
            try { _ctsBeacon?.Cancel(); } catch { }
            try { _udpSend?.Dispose(); } catch { }
            _ctsBeacon = null; _udpSend = null;
        }

        public static string? GetLocalIPv4()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var uni in ipProps.UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(uni.Address))
                            return uni.Address.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        public void Dispose()
        {
            StopListen();
            StopHostBeacon();
        }
    }
}