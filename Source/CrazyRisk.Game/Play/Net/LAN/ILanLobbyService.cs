#nullable enable
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CrazyRiskGame.Net.LAN
{
    /// <summary>
    /// Interfaz del servicio de lobby LAN (descubrimiento UDP + transporte TCP).
    /// No crea UI; lo consumen las pantallas de Lobby.
    /// </summary>
    public interface ILanLobbyService : IDisposable
    {
        /// <summary>Puerto TCP en el que hostear (para conexiones) y anunciar (por UDP).</summary>
        int HostPort { get; }

        /// <summary>Nombre del lobby/anfitrión a anunciar.</summary>
        string LobbyName { get; set; }

        /// <summary>¿Está activo el host (TCP listener + beacons UDP)?</summary>
        bool IsHosting { get; }

        /// <summary>¿Está activo el cliente (conectado a un host)?</summary>
        bool IsClientConnected { get; }

        /// <summary>Señales desde el servicio (alta/caída cliente, beacons recibidos, errores).</summary>
        event Action<LobbyEvent>? OnLobbyEvent;

        /// <summary>Comienza a hostear (TCP) y anunciarse (UDP beacons).</summary>
        Task StartHostAsync(int tcpPort, CancellationToken ct = default);

        /// <summary>Detiene host y anuncios.</summary>
        Task StopHostAsync();

        /// <summary>Escanea LAN por beacons de lobbies activos durante "timeoutMs".</summary>
        Task DiscoverAsync(int timeoutMs, Action<LobbyBeacon>? onFound, CancellationToken ct = default);

        /// <summary>Conecta como cliente al host indicado.</summary>
        Task ConnectAsync(IPAddress host, int port, CancellationToken ct = default);

        /// <summary>Desconecta cliente (si aplica).</summary>
        Task DisconnectAsync();

        /// <summary>Envía un mensaje liviano (texto) a través del socket actual (cliente o broadcast a clientes si host).</summary>
        Task SendAsync(string message, CancellationToken ct = default);
    }
}