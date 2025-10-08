#nullable enable
using System;
using System.Net;

namespace CrazyRiskGame.Net.LAN
{
    /// <summary>Tipos de eventos que emite el servicio LAN.</summary>
    public enum LobbyEventType
    {
        Info,
        Error,
        HostStarted,
        HostStopped,
        ClientConnected,
        ClientDisconnected,
        BeaconFound,
        Message
    }

    /// <summary>Evento del lobby (para log/estado en UI).</summary>
    public sealed class LobbyEvent
    {
        public LobbyEventType Type { get; init; }
        public string Message { get; init; } = "";
        public IPEndPoint? RemoteEndPoint { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        public static LobbyEvent Info(string msg) => new() { Type = LobbyEventType.Info, Message = msg };
        public static LobbyEvent Error(string msg) => new() { Type = LobbyEventType.Error, Message = msg };
        public static LobbyEvent HostStarted(int port) => new() { Type = LobbyEventType.HostStarted, Message = $"Host @ {port}" };
        public static LobbyEvent HostStopped() => new() { Type = LobbyEventType.HostStopped, Message = "Host stopped" };
        public static LobbyEvent ClientConnected(IPEndPoint ep) => new() { Type = LobbyEventType.ClientConnected, RemoteEndPoint = ep, Message = $"Client {ep}" };
        public static LobbyEvent ClientDisconnected(IPEndPoint ep) => new() { Type = LobbyEventType.ClientDisconnected, RemoteEndPoint = ep, Message = $"Client left {ep}" };
        public static LobbyEvent Beacon(LobbyBeacon b) => new() { Type = LobbyEventType.BeaconFound, Message = $"{b.Name} @ {b.Address}:{b.Port}" };
        public static LobbyEvent Msg(IPEndPoint? ep, string msg) => new() { Type = LobbyEventType.Message, RemoteEndPoint = ep, Message = msg };
    }

    /// <summary>Paquete de anuncio (beacon UDP) sencillo.</summary>
    public sealed class LobbyBeacon
    {
        public string Name { get; init; } = "CrazyRisk Lobby";
        public IPAddress Address { get; init; } = IPAddress.Loopback;
        public int Port { get; init; }
    }
}