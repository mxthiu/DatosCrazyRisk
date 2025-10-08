#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CrazyRiskGame.Net.Lan
{
    /// <summary>
    /// Lobby LAN por UDP (broadcast). Host + 2 clientes (3 jugadores).
    /// - Descubrimiento: cliente envía DISCOVER (broadcast) y host responde con DISCOVER_REPLY.
    /// - Unión y control: todo por UDP (al mismo puerto); el host mantiene el estado y lo “empuja”.
    ///
    /// Protocolo (sobre JSON):
    ///   Envelope: { "t": int, "p": string }  // t = PacketType, p = payload JSON
    ///
    ///   DISCOVER (client→broadcast):          { }
    ///   DISCOVER_REPLY (host→client unicast): { host, players, locked[] }
    ///
    ///   JOIN_REQUEST (client→host):           { name, avatar }
    ///   JOIN_APPROVED (host→client):          { ok, slot, reason? }
    ///
    ///   CHANGE_AVATAR (client→host):          { avatar }
    ///   READY (client→host):                  { ready }
    ///   LEAVE (client→host):                  { }
    ///
    ///   STATE (host→client):                  LobbyState completo
    ///   STATE_REQUEST (client→host):          { } (cliente fuerza “push” de estado)
    /// </summary>
    public sealed class LanLobby : IDisposable
    {
        // ========================= Config de red =========================
        public const int BROADCAST_PORT = 33333;  // puerto UDP compartido
        private const int RX_TIMEOUT_MS = 200;    // tiempo de espera en Receive()
        public const int TICK_MS = 100;           // periodo de broadcast de estado del host
        public const int TIMEOUT_MS = 5000;       // desconexión por inactividad

        // ========================= Rol / Estado =========================
        public LobbyRole Role { get; private set; } = LobbyRole.None;
        public LobbyState State { get; private set; } = new();

        public string LocalName { get; private set; } = $"Player-{Environment.MachineName}";
        public int LocalAvatar { get; private set; } = -1;
        public bool LocalReady { get; private set; } = false;

        // ========================= Red / Threads =========================
        private UdpClient? udp;
        private IPEndPoint anyEP = new IPEndPoint(IPAddress.Any, 0);
        private Thread? rxThread;
        private volatile bool stopping;

        // Host
        private long lastBroadcastTicks;

        // Client
        private IPEndPoint? connectedHost;

        // Sync
        private readonly object sync = new();

        // ========================= Eventos UI =========================
        /// <summary> Cambios generales de modo/rol/state. </summary>
        public event Action? OnStateChanged;

        /// <summary> Lista de jugadores cambió (nombres, avatars, ready). </summary>
        public event Action? OnPlayersChanged;

        /// <summary> Logs útiles para depurar/red. </summary>
        public event Action<string>? OnLog;

        /// <summary> Disparado cuando el host establece GameStarting=true. </summary>
        public event Action? OnGameStarting;

        // =================================================================
        // ======================== API PÚBLICA =============================
        // =================================================================

        /// <summary>Arranca como HOST (slot 0 ocupado por el host). Devuelve true si OK.</summary>
        public bool StartHost(string hostDisplayName = "Host")
        {
            Stop(); // limpiar cualquier sesión previa

            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.EnableBroadcast = true;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, BROADCAST_PORT));
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("[LOBBY] UDP bind host falló: " + ex.Message);
                Stop();
                return false;
            }

            Role = LobbyRole.Host;
            State = new LobbyState
            {
                HostName = string.IsNullOrWhiteSpace(hostDisplayName) ? "Host" : hostDisplayName
            };

            // El host ocupa el slot 0
            State.Players[0] = new PlayerSlot
            {
                Id = 0,
                Name = State.HostName,
                AvatarId = LocalAvatar, // -1 = sin elegir
                Ready = false,
                IsHost = true,
                LastSeenUtcMs = NowMs()
            };

            StartRxThread();

            OnLog?.Invoke($"[LOBBY] Host escuchando UDP:{BROADCAST_PORT}");
            OnStateChanged?.Invoke();
            OnPlayersChanged?.Invoke();
            return true;
        }

        /// <summary>Arranca como CLIENTE (modo descubrimiento). Devuelve true si OK.</summary>
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
                // Recibir desde cualquier puerto ephemeral (no colisionar con host)
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("[LOBBY] UDP cliente falló: " + ex.Message);
                Stop();
                return false;
            }

            Role = LobbyRole.Client;
            LocalName = displayName;
            LocalReady = false;
            LocalAvatar = -1;
            State = new LobbyState();

            StartRxThread();

            OnLog?.Invoke("[LOBBY] Cliente listo para descubrir hosts.");
            OnStateChanged?.Invoke();
            return true;
        }

        /// <summary>Cliente: envía DISCOVER (broadcast) y escucha ~windowMs los DISCOVER_REPLY.</summary>
        public List<DiscoveredHost> DiscoverHosts(int windowMs = 450)
        {
            var list = new List<DiscoveredHost>();
            if (udp == null || Role != LobbyRole.Client) return list;

            try
            {
                // Enviar DISCOVER
                var pkt = MakePacket(PacketType.Discover, new { name = LocalName });
                udp.Send(pkt, pkt.Length, new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));

                // Ventana de escucha
                var until = Environment.TickCount64 + windowMs;
                udp.Client.ReceiveTimeout = Math.Min(150, windowMs);

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
                    catch (SocketException) { /* timeout parcial: seguir */ }
                    catch { /* ignorar */ }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("[LOBBY] Discover error: " + ex.Message);
            }

            return list;
        }

        /// <summary>Cliente: solicita unirse a un host descubierto. El host contestará JOIN_APPROVED.</summary>
        public void JoinHost(IPEndPoint hostEndPoint, int desiredAvatar)
        {
            if (Role != LobbyRole.Client || udp == null) return;

            LocalAvatar = desiredAvatar;
            LocalReady = false;
            connectedHost = hostEndPoint;

            var join = new { name = LocalName, avatar = LocalAvatar };
            SendToHost(PacketType.JoinRequest, join);

            OnLog?.Invoke($"[LOBBY] Join request → {hostEndPoint.Address}");
        }

        /// <summary>Cliente: solicita cambio de avatar.</summary>
        public void RequestAvatar(int desiredAvatar)
        {
            if (Role != LobbyRole.Client || udp == null || connectedHost == null) return;
            LocalAvatar = desiredAvatar;
            SendToHost(PacketType.ChangeAvatar, new { avatar = desiredAvatar });
        }

        /// <summary>Marca listo/no listo. En host actualiza slot 0; en cliente envía READY al host.</summary>
        public void SetReady(bool ready)
        {
            LocalReady = ready;

            if (Role == LobbyRole.Client && udp != null)
            {
                SendToHost(PacketType.Ready, new { ready });
            }
            else if (Role == LobbyRole.Host)
            {
                lock (sync)
                {
                    State.Players[0].Ready = ready;
                    BroadcastState();
                    OnPlayersChanged?.Invoke();
                }
            }
        }

        /// <summary>Host: inicia la partida si todos están “ready”. Pone GameStarting=true y lanza evento.</summary>
        public bool HostStartGame()
        {
            if (Role != LobbyRole.Host) return false;
            lock (sync)
            {
                if (!State.AllReady())
                {
                    OnLog?.Invoke("[LOBBY] No todos están READY.");
                    return false;
                }
                State.GameStarting = true;
                BroadcastState();
            }
            OnLog?.Invoke("[LOBBY] GameStarting=TRUE (anunciado a clientes).");
            OnGameStarting?.Invoke(); // la UI debe cambiar a la pantalla de juego
            return true;
        }

        /// <summary>Debe ser llamado periódicamente desde Game.Update() cuando eres host.</summary>
        public void Update()
        {
            if (Role != LobbyRole.Host) return;

            lock (sync)
            {
                // Expulsar por timeout
                long now = NowMs();
                for (int i = 1; i < 3; i++)
                {
                    if (!State.Players[i].IsOccupied) continue;
                    if (now - State.Players[i].LastSeenUtcMs > TIMEOUT_MS)
                    {
                        OnLog?.Invoke($"[LOBBY] Timeout en slot {i}, liberando.");
                        State.Players[i] = PlayerSlot.Empty(i);
                        OnPlayersChanged?.Invoke();
                    }
                }

                // Broadcast de estado
                if (Environment.TickCount64 - lastBroadcastTicks > TICK_MS)
                {
                    BroadcastState();
                    lastBroadcastTicks = Environment.TickCount64;
                }
            }
        }

        /// <summary>Cliente: abandona. Host: resetea lobby.</summary>
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
                if (rxThread != null && rxThread.IsAlive) rxThread.Join(250);
            }
            catch { }
            try { udp?.Close(); } catch { }
            try { udp?.Dispose(); } catch { }
            udp = null;

            rxThread = null;
            Role = LobbyRole.None;
            connectedHost = null;

            // No destruimos State para que la UI pueda leer último estado;
            // si quieres reset duro, descomenta:
            // State = new LobbyState();

            OnStateChanged?.Invoke();
        }

        // =================================================================
        // ======================= RX / TX Interno ==========================
        // =================================================================

        private void StartRxThread()
        {
            stopping = false;
            rxThread = new Thread(RxLoop) { IsBackground = true, Name = "LanLobby-RX" };
            rxThread.Start();
        }

        private void RxLoop()
        {
            if (udp == null) return;
            udp.Client.ReceiveTimeout = RX_TIMEOUT_MS;

            while (!stopping)
            {
                try
                {
                    var buf = udp.Receive(ref anyEP);
                    var msg = Encoding.UTF8.GetString(buf);

                    if (!TryParsePacket(msg, out var type, out var payload))
                        continue;

                    if (Role == LobbyRole.Host)
                        HandleAsHost(type, payload, anyEP);
                    else if (Role == LobbyRole.Client)
                        HandleAsClient(type, payload, anyEP);
                }
                catch (SocketException) { /* timeout -> seguir */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    OnLog?.Invoke("[RX] " + ex.Message);
                }
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
                                Remote = new IPEndPoint(from.Address, BROADCAST_PORT)
                            };
                            BroadcastState();
                            OnPlayersChanged?.Invoke();
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
                        OnPlayersChanged?.Invoke();
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
                        OnPlayersChanged?.Invoke();
                        break;
                    }

                    case PacketType.Leave:
                    {
                        int slot = State.FindSlotByEndPoint(from);
                        if (slot >= 0)
                        {
                            State.Players[slot] = PlayerSlot.Empty(slot);
                            BroadcastState();
                            OnPlayersChanged?.Invoke();
                        }
                        break;
                    }

                    case PacketType.StateRequest:
                    {
                        // cliente pide estado forzado (por latencia al entrar)
                        var force = MakePacket(PacketType.State, State);
                        udp?.Send(force, force.Length, from);
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
                        connectedHost = new IPEndPoint(from.Address, BROADCAST_PORT);
                        // pedir estado de arranque
                        SendToHost(PacketType.StateRequest, new { });
                        OnLog?.Invoke("[LOBBY] Join aprobado. Slot=" + ok.slot);
                    }
                    else
                    {
                        OnLog?.Invoke("[LOBBY] Join rechazado: " + (ok.reason ?? "desconocido"));
                        connectedHost = null;
                    }
                    break;
                }

                case PacketType.State:
                {
                    var st = JsonSerializer.Deserialize<LobbyState>(payload);
                    if (st != null)
                    {
                        lock (sync) State = st;
                        OnPlayersChanged?.Invoke();

                        if (st.GameStarting)
                            OnGameStarting?.Invoke();
                    }
                    break;
                }
            }
        }

        private void BroadcastState()
        {
            if (udp == null) return;
            var pkt = MakePacket(PacketType.State, State);

            // enviar a cada cliente conocido (slots 1..2)
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

        // =================================================================
        // ============================ Utils ===============================
        // =================================================================

        private static byte[] MakePacket(PacketType type, object body)
        {
            var env = new Envelope { t = (int)type, p = JsonSerializer.Serialize(body) };
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

        // =================================================================
        // ============================ DTOs ================================
        // =================================================================

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
            // Discovery
            Discover = 1, DiscoverReply = 2,
            // Join / state
            JoinRequest = 10, JoinApproved = 11,
            ChangeAvatar = 12, Ready = 13, Leave = 14,
            StateRequest = 15, State = 16
        }

        // ======================= Modelo de estado =========================
        public sealed class LobbyState
        {
            public string? HostName { get; set; }

            // 3 slots: 0=host, 1..2 clientes
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
                for (int i = 0; i < Players.Length; i++)
                    if (Players[i].IsOccupied) c++;
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
                // aquí definimos “todos” = los ocupados (host + todos los conectados)
                for (int i = 0; i < Players.Length; i++)
                {
                    if (!Players[i].IsOccupied) return false;
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

            // Host-only: tracking
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

    // ================= Resultados de descubrimiento =====================
    public sealed class DiscoveredHost
    {
        public IPEndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, LanLobby.BROADCAST_PORT);
        public string HostName { get; set; } = "Host";
        public int PlayerCount { get; set; }
        public int[] LockedAvatars { get; set; } = Array.Empty<int>();
        public override string ToString() => $"{HostName} @ {EndPoint.Address} ({PlayerCount}/3)";
    }
}