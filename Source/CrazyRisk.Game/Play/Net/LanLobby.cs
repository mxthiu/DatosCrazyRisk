#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CrazyRiskGame.Play.Net
{
    /// <summary>
    /// Lobby LAN simple (host + hasta 2 clientes).
    /// Descubrimiento por broadcast UDP y sincronización por UDP.
    /// </summary>
    public sealed class LanLobby : IDisposable
    {
        // ------------------- Config red -------------------
        public const int BROADCAST_PORT = 33333;   // descubrimiento y control
        public const int TICK_MS        = 100;     // frecuencia de envío estado host
        public const int TIMEOUT_MS     = 5000;    // timeout para clientes “muertos”

        // ------------------- Rol actual -------------------
        public LobbyRole Role { get; private set; } = LobbyRole.None;

        // ------------------- Estado compartido -------------------
        public LobbyState State { get; private set; } = new();

        // Identidad local (nombre y avatar deseado)
        public string LocalName { get; private set; } = $"Player-{Environment.MachineName}";
        public int LocalAvatar { get; private set; } = -1;
        public bool LocalReady { get; private set; } = false;

        // Red
        private UdpClient? udp;
        private IPEndPoint anyEP = new IPEndPoint(IPAddress.Any, 0);

        // Hilo de RX
        private Thread? rxThread;
        private volatile bool stopping;

        // Host: última transmisión de estado
        private long lastBroadcastTicks;

        // Cliente: host seleccionado (descubierto)
        private IPEndPoint? connectedHost;

        // Mutex
        private readonly object sync = new object();

        // =================== API pública ===================

        /// <summary> Host: inicia lobby como servidor. </summary>
        public bool StartHost(string hostDisplayName = "Host")
        {
            Stop();
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.EnableBroadcast = true;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, BROADCAST_PORT));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LOBBY] No se pudo abrir puerto UDP: " + ex.Message);
                Stop();
                return false;
            }

            Role = LobbyRole.Host;
            State = new LobbyState
            {
                HostName = string.IsNullOrWhiteSpace(hostDisplayName) ? "Host" : hostDisplayName
            };

            // Host ocupa slot 0
            State.Players[0] = new PlayerSlot
            {
                Id = 0,
                Name = State.HostName,
                AvatarId = LocalAvatar,    // -1 = sin elegir
                Ready = false,
                IsHost = true,
                LastSeenUtcMs = NowMs()
            };

            StartRxThread();
            Console.WriteLine("[LOBBY] Host iniciado en UDP:" + BROADCAST_PORT);
            return true;
        }

        /// <summary> Cliente: entra al modo discovery; se auto-conecta al primer host visible cuando llames JoinHost(). </summary>
        public bool StartClient(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = $"Player-{Environment.MachineName}";

            Stop();
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.EnableBroadcast = true;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                // NO bind al puerto del host; recibir en cualquiera:
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LOBBY] No se pudo abrir UDP cliente: " + ex.Message);
                Stop();
                return false;
            }

            Role = LobbyRole.Client;
            LocalName = displayName;
            State = new LobbyState();

            StartRxThread();
            Console.WriteLine("[LOBBY] Cliente iniciado.");
            return true;
        }

        /// <summary> Cliente: descubre host vía broadcast. Devuelve lista de hosts avistados en ~400ms. </summary>
        public List<DiscoveredHost> DiscoverHosts(int windowMs = 400)
        {
            var list = new List<DiscoveredHost>();
            if (udp == null || Role != LobbyRole.Client) return list;

            try
            {
                // Enviar “DISCOVER”
                var pkt = MakePacket(PacketType.Discover, new { name = LocalName });
                udp.Send(pkt, pkt.Length, new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));

                // Ventana de escucha
                var until = Environment.TickCount64 + windowMs;
                udp.Client.ReceiveTimeout = 100;

                while (Environment.TickCount64 < until)
                {
                    try
                    {
                        var buf = udp.Receive(ref anyEP);
                        var msg = Encoding.UTF8.GetString(buf);
                        if (!TryParsePacket(msg, out var type, out var payload)) continue;

                        if (type == PacketType.DiscoverReply)
                        {
                            var host = JsonSerializer.Deserialize<DiscoverReply>(payload);
                            if (host != null)
                            {
                                list.Add(new DiscoveredHost
                                {
                                    EndPoint = new IPEndPoint(anyEP.Address, BROADCAST_PORT),
                                    HostName = host.host,
                                    PlayerCount = host.players,
                                    LockedAvatars = host.locked ?? Array.Empty<int>()
                                });
                            }
                        }
                    }
                    catch { /*silencio*/ }
                }
            }
            catch { }

            return list;
        }

        /// <summary> Cliente: se une a un host (el primero de DiscoverHosts o uno específico). </summary>
        public void JoinHost(IPEndPoint hostEndPoint, int desiredAvatar)
        {
            if (Role != LobbyRole.Client || udp == null) return;

            LocalAvatar = desiredAvatar;
            LocalReady = false;
            connectedHost = hostEndPoint;

            var join = new
            {
                name = LocalName,
                avatar = LocalAvatar
            };
            SendToHost(PacketType.JoinRequest, join);
        }

        /// <summary> Cliente: cambia avatar (si el host lo acepta). </summary>
        public void RequestAvatar(int desiredAvatar)
        {
            if (Role != LobbyRole.Client || udp == null || connectedHost == null) return;
            LocalAvatar = desiredAvatar;
            SendToHost(PacketType.ChangeAvatar, new { avatar = desiredAvatar });
        }

        /// <summary> Cliente: set ready. </summary>
        public void SetReady(bool ready)
        {
            LocalReady = ready;
            if (Role == LobbyRole.Client && udp != null)
                SendToHost(PacketType.Ready, new { ready });
            else if (Role == LobbyRole.Host)
            {
                // Host es slot 0
                lock (sync)
                {
                    State.Players[0].Ready = ready;
                    BroadcastState();
                }
            }
        }

        /// <summary> Host: inicia partida (si todos ready). </summary>
        public bool HostStartGame()
        {
            if (Role != LobbyRole.Host) return false;
            lock (sync)
            {
                if (!State.AllReady()) return false;
                State.GameStarting = true;
                BroadcastState();
            }
            return true;
        }

        /// <summary> Llamar periódicamente desde tu Update(). </summary>
        public void Update()
        {
            if (Role == LobbyRole.Host)
            {
                lock (sync)
                {
                    // Kick clientes con timeout
                    long now = NowMs();
                    for (int i = 1; i < 3; i++)
                    {
                        if (!State.Players[i].IsOccupied) continue;
                        if (now - State.Players[i].LastSeenUtcMs > TIMEOUT_MS)
                        {
                            Console.WriteLine($"[LOBBY] Cliente timeout: slot {i}");
                            State.Players[i] = PlayerSlot.Empty(i);
                        }
                    }

                    // Enviar estado cada TICK
                    if (Environment.TickCount64 - lastBroadcastTicks > TICK_MS)
                    {
                        BroadcastState();
                        lastBroadcastTicks = Environment.TickCount64;
                    }
                }
            }
        }

        public void Leave()
        {
            if (Role == LobbyRole.Client && connectedHost != null)
                SendToHost(PacketType.Leave, new { });

            Stop();
        }

        public void Stop() => Dispose();

        public void Dispose()
        {
            try
            {
                stopping = true;
                if (rxThread != null && rxThread.IsAlive) rxThread.Join(200);
            }
            catch { }
            try { udp?.Close(); } catch { }
            try { udp?.Dispose(); } catch { }
            udp = null;
            rxThread = null;
            Role = LobbyRole.None;
            connectedHost = null;
        }

        // =================== RX/TX ===================

        private void StartRxThread()
        {
            stopping = false;
            rxThread = new Thread(RxLoop) { IsBackground = true, Name = "LanLobby-RX" };
            rxThread.Start();
        }

        private void RxLoop()
        {
            if (udp == null) return;
            udp.Client.ReceiveTimeout = 200;

            while (!stopping)
            {
                try
                {
                    var buf = udp.Receive(ref anyEP);
                    var msg = Encoding.UTF8.GetString(buf);
                    if (!TryParsePacket(msg, out var type, out var payload)) continue;

                    if (Role == LobbyRole.Host)
                        HandleAsHost(type, payload, anyEP);
                    else if (Role == LobbyRole.Client)
                        HandleAsClient(type, payload, anyEP);
                }
                catch (SocketException) { /* timeout; seguir */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Console.WriteLine("[LOBBY RX] " + ex.Message); }
            }
        }

        private void HandleAsHost(PacketType type, string payload, IPEndPoint from)
        {
            lock (sync)
            {
                switch (type)
                {
                    case PacketType.Discover:
                    {
                        // Responder con info resumida del lobby
                        var reply = new DiscoverReply
                        {
                            host = State.HostName ?? "Host",
                            players = State.PlayerCount(),
                            locked = State.LockedAvatars()
                        };
                        var data = MakePacket(PacketType.DiscoverReply, reply);
                        udp?.Send(data, data.Length, from);
                        break;
                    }

                    case PacketType.JoinRequest:
                    {
                        var req = JsonSerializer.Deserialize<JoinRequest>(payload);
                        if (req == null) break;

                        // Primer slot libre (1..2)
                        int slot = State.FirstFreeSlot();
                        var approved = new JoinApproved
                        {
                            ok = slot >= 0,
                            slot = slot,
                            reason = slot < 0 ? "Lobby completo" : null
                        };
                        var data = MakePacket(PacketType.JoinApproved, approved);
                        udp?.Send(data, data.Length, from);

                        if (slot >= 0)
                        {
                            State.Players[slot] = new PlayerSlot
                            {
                                Id = slot,
                                Name = San(req.name, 24),
                                AvatarId = req.avatar,
                                Ready = false,
                                IsHost = false,
                                LastSeenUtcMs = NowMs(),
                                Remote = from
                            };
                            BroadcastState();
                        }
                        break;
                    }

                    case PacketType.ChangeAvatar:
                    {
                        var ch = JsonSerializer.Deserialize<ChangeAvatar>(payload);
                        if (ch == null) break;

                        int slot = State.FindSlotByEndPoint(from);
                        if (slot < 0) break;

                        if (State.AvatarIsFree(ch.avatar, exceptSlot: slot))
                            State.Players[slot].AvatarId = ch.avatar;

                        State.Players[slot].LastSeenUtcMs = NowMs();
                        BroadcastState();
                        break;
                    }

                    case PacketType.Ready:
                    {
                        var rd = JsonSerializer.Deserialize<ClientReady>(payload);
                        if (rd == null) break;

                        int slot = State.FindSlotByEndPoint(from);
                        if (slot < 0) break;

                        State.Players[slot].Ready = rd.ready;
                        State.Players[slot].LastSeenUtcMs = NowMs();
                        BroadcastState();
                        break;
                    }

                    case PacketType.Leave:
                    {
                        int slot = State.FindSlotByEndPoint(from);
                        if (slot >= 0)
                        {
                            State.Players[slot] = PlayerSlot.Empty(slot);
                            BroadcastState();
                        }
                        break;
                    }
                }
            }
        }

        private void HandleAsClient(PacketType type, string payload, IPEndPoint from)
        {
            switch (type)
            {
                case PacketType.JoinApproved:
                {
                    var ok = JsonSerializer.Deserialize<JoinApproved>(payload);
                    if (ok == null) break;

                    if (ok.ok && ok.slot >= 0)
                    {
                        // Conectado: guardar “host”
                        connectedHost = new IPEndPoint(from.Address, BROADCAST_PORT);
                        // Pedir estado al host (lo enviará periódicamente, pero forzamos)
                        SendToHost(PacketType.StateRequest, new { });
                    }
                    else
                    {
                        Console.WriteLine("[LOBBY] Join rechazado: " + (ok.reason ?? "desconocido"));
                        connectedHost = null;
                    }
                    break;
                }

                case PacketType.State:
                {
                    var st = JsonSerializer.Deserialize<LobbyState>(payload);
                    if (st != null)
                        lock (sync) State = st;
                    break;
                }
            }
        }

        private void BroadcastState()
        {
            if (udp == null) return;

            // A todos los clientes conocidos
            var pkt = MakePacket(PacketType.State, State);

            // Enviar a cada “Remote” válido
            for (int i = 1; i < 3; i++)
            {
                var p = State.Players[i];
                if (!p.IsOccupied || p.Remote == null) continue;

                try { udp.Send(pkt, pkt.Length, p.Remote); } catch { }
            }
        }

        private void SendToHost(PacketType type, object body)
        {
            if (udp == null || connectedHost == null) return;
            var pkt = MakePacket(type, body);
            try { udp.Send(pkt, pkt.Length, connectedHost); } catch { }
        }

        // =================== Util ===================

        private static byte[] MakePacket(PacketType type, object body)
        {
            var env = new Envelope
            {
                t = (int)type,
                p = JsonSerializer.Serialize(body)
            };
            var json = JsonSerializer.Serialize(env);
            return Encoding.UTF8.GetBytes(json);
        }

        private static bool TryParsePacket(string json, out PacketType type, out string payload)
        {
            type = PacketType.Unknown;
            payload = "";
            try
            {
                var env = JsonSerializer.Deserialize<Envelope>(json);
                if (env == null) return false;
                type = (PacketType)env.t;
                payload = env.p ?? "";
                return true;
            }
            catch { return false; }
        }

        private static string San(string s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : t;
        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // =================== DTOs / Model ===================

        private sealed class Envelope { public int t { get; set; } public string? p { get; set; } }

        private sealed class JoinRequest { public string name { get; set; } = ""; public int avatar { get; set; } }
        private sealed class JoinApproved { public bool ok { get; set; } public int slot { get; set; } public string? reason { get; set; } }
        private sealed class ChangeAvatar { public int avatar { get; set; } }
        private sealed class ClientReady { public bool ready { get; set; } }
        private sealed class DiscoverReply { public string host { get; set; } = "Host"; public int players { get; set; } public int[]? locked { get; set; } }

        public enum LobbyRole { None, Host, Client }

        public enum PacketType
        {
            Unknown = 0,
            // Descubrimiento
            Discover = 1, DiscoverReply = 2,
            // Ciclo de unión/estado
            JoinRequest = 10, JoinApproved = 11,
            ChangeAvatar = 12, Ready = 13, Leave = 14,
            StateRequest = 15, State = 16
        }

        public sealed class LobbyState
        {
            public string? HostName { get; set; }

            // 3 slots máximo: 0=host, 1-2 clientes
            public PlayerSlot[] Players { get; set; } = new[]
            {
                PlayerSlot.Empty(0),
                PlayerSlot.Empty(1),
                PlayerSlot.Empty(2)
            };

            public bool GameStarting { get; set; }

            public int PlayerCount()
            {
                int c = 0;
                for (int i = 0; i < Players.Length; i++) if (Players[i].IsOccupied) c++;
                return c;
            }

            public int FirstFreeSlot()
            {
                for (int i = 1; i < Players.Length; i++)
                    if (!Players[i].IsOccupied) return i;
                return -1;
            }

            public int FindSlotByEndPoint(IPEndPoint ep)
            {
                for (int i = 1; i < Players.Length; i++)
                {
                    var p = Players[i];
                    if (!p.IsOccupied || p.Remote == null) continue;
                    if (p.Remote.Address.Equals(ep.Address)) return i;
                }
                return -1;
            }

            public bool AllReady()
            {
                for (int i = 0; i < Players.Length; i++)
                {
                    if (!Players[i].IsOccupied) return false; // queremos los 3
                    if (!Players[i].Ready) return false;
                }
                return true;
            }

            public bool AvatarIsFree(int avatar, int exceptSlot = -1)
            {
                if (avatar < 0) return true;
                for (int i = 0; i < Players.Length; i++)
                {
                    if (i == exceptSlot) continue;
                    if (Players[i].IsOccupied && Players[i].AvatarId == avatar) return false;
                }
                return true;
            }

            public int[] LockedAvatars()
            {
                var list = new List<int>();
                for (int i = 0; i < Players.Length; i++)
                    if (Players[i].IsOccupied && Players[i].AvatarId >= 0)
                        list.Add(Players[i].AvatarId);
                return list.ToArray();
            }
        }

        public struct PlayerSlot
        {
            public int Id;
            public string Name;
            public int AvatarId;
            public bool Ready;
            public bool IsHost;

            // Host-only: tracking del cliente
            public long LastSeenUtcMs;
            public IPEndPoint? Remote;

            public bool IsOccupied => !string.IsNullOrWhiteSpace(Name);

            public static PlayerSlot Empty(int id) => new PlayerSlot
            {
                Id = id,
                Name = "",
                AvatarId = -1,
                Ready = false,
                IsHost = id == 0,
                LastSeenUtcMs = 0,
                Remote = null
            };
        }
    }

    // -------------- Descubrimiento: resultado --------------
    public sealed class DiscoveredHost
    {
        public IPEndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, LanLobby.BROADCAST_PORT);
        public string HostName { get; set; } = "Host";
        public int PlayerCount { get; set; }
        public int[] LockedAvatars { get; set; } = Array.Empty<int>();
        public override string ToString() => $"{HostName} @ {EndPoint.Address} ({PlayerCount}/3)";
    }
}