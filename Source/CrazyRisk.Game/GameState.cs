#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CrazyRiskGame
{
    internal sealed class Player
    {
        public int Id { get; }
        public string Name { get; }
        public Color Color { get; }

        public Player(int id, string name, Color color)
        {
            Id = id; Name = name; Color = color;
        }
    }

    /// <summary>
    /// Estado del mundo: dueño y tropas por territorio, reglas básicas de movimiento/ataque.
    /// </summary>
    internal sealed class WorldState
    {
        private readonly Dictionary<string, int> ownerById = new();   // territorio -> playerId
        private readonly Dictionary<string, int> troopsById = new();  // territorio -> tropas
        private readonly Func<string, IEnumerable<string>> getNeighbors;
        private readonly Random rng = new();

        public IReadOnlyDictionary<string, int> Owners => ownerById;
        public IReadOnlyDictionary<string, int> Troops => troopsById;

        public WorldState(Func<string, IEnumerable<string>> neighborProvider)
        {
            getNeighbors = neighborProvider;
        }

        public void Reset(IEnumerable<string> ids)
        {
            ownerById.Clear();
            troopsById.Clear();
            foreach (var id in ids)
            {
                ownerById[id] = -1;
                troopsById[id] = 0;
            }
        }

        /// <summary>
        /// Asigna dueños de forma round-robin y pone tropas iniciales (min 1).
        /// </summary>
        public void QuickDistribute(IReadOnlyList<string> territoryIds, IReadOnlyList<Player> players, int baseTroopsPerTerritory = 3)
        {
            if (players.Count == 0) return;

            for (int i = 0; i < territoryIds.Count; i++)
            {
                var tid = territoryIds[i];
                var pid = players[i % players.Count].Id;
                ownerById[tid] = pid;
                troopsById[tid] = Math.Max(1, baseTroopsPerTerritory);
            }
        }

        public int GetOwner(string territoryId) => ownerById.TryGetValue(territoryId, out var p) ? p : -1;
        public int GetTroops(string territoryId) => troopsById.TryGetValue(territoryId, out var t) ? t : 0;

        public void SetOwner(string territoryId, int playerId) => ownerById[territoryId] = playerId;
        public void SetTroops(string territoryId, int troops) => troopsById[territoryId] = Math.Max(0, troops);

        public bool AreNeighbors(string a, string b)
        {
            foreach (var n in getNeighbors(a))
                if (n == b) return true;
            return false;
        }

        /// <summary>
        /// Mover tropas entre territorios del mismo dueño (requiere vecinos).
        /// </summary>
        public bool Move(string from, string to, int amount)
        {
            if (amount <= 0) return false;
            if (!AreNeighbors(from, to)) return false;

            int ownerFrom = GetOwner(from);
            if (ownerFrom < 0 || ownerFrom != GetOwner(to)) return false;

            int tf = GetTroops(from);
            if (tf <= amount) return false; // dejar al menos 1 en origen

            SetTroops(from, tf - amount);
            SetTroops(to, GetTroops(to) + amount);
            return true;
        }

        /// <summary>
        /// Ataque simplificado: si gana atacante, toma el territorio con 1 tropa (el resto se queda).
        /// Retorna true si cambió de dueño (conquista).
        /// </summary>
        public bool Attack(string from, string to)
        {
            if (!AreNeighbors(from, to)) return false;

            int attOwner = GetOwner(from);
            int defOwner = GetOwner(to);
            if (attOwner < 0 || defOwner < 0 || attOwner == defOwner) return false;

            int attTroops = GetTroops(from);
            int defTroops = GetTroops(to);
            if (attTroops < 2 || defTroops < 1) return false; // necesitas al menos 2 para atacar (1 se queda)

            // Dados simplificados: 1d6 atacante vs 1d6 defensor + bonificación por tamaño
            int attDie = rng.Next(1, 7) + (attTroops >= defTroops ? 1 : 0);
            int defDie = rng.Next(1, 7) + (defTroops > attTroops ? 1 : 0);

            if (attDie > defDie)
            {
                // conquista: to pasa a attOwner con 1 tropa; from pierde 1 tropa por “marcha”
                SetOwner(to, attOwner);
                SetTroops(to, 1);
                SetTroops(from, attTroops - 1);
                return true;
            }
            else
            {
                // defensor resiste: atacante pierde 1 tropa
                SetTroops(from, attTroops - 1);
                return false;
            }
        }
    }
}
