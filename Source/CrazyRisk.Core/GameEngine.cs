// File: Source/CrazyRisk.Core/GameEngine.cs
#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using CrazyRisk.Core.DataStructures;

namespace CrazyRisk.Core
{
    // ========================= Map helpers por reflexión (propiedades y CAMPOS) =========================
    // Permite leer Map.Territories (propiedad o campo), Territory.id/Id y Territory.neighbors/Neighbors
    // sin acoplarse al tipo concreto que tengas en Map.cs
    internal static class MapExtensions
    {
        // Devuelve la lista de "objetos territorio" crudos (del array/lista Map.Territories)
        public static IEnumerable<object> GetRawTerritories(this Map map)
        {
            var tp = map.GetType();

            // 1) Propiedad "Territories"
            var p = tp.GetProperty("Territories", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null)
            {
                var value = p.GetValue(map);
                if (value is System.Collections.IEnumerable en)
                {
                    foreach (var t in en) if (t != null) yield return t;
                    yield break;
                }
            }

            // 2) Campo "Territories"
            var f = tp.GetField("Territories", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (f != null)
            {
                var value = f.GetValue(map);
                if (value is System.Collections.IEnumerable en)
                {
                    foreach (var t in en) if (t != null) yield return t;
                    yield break;
                }
            }
        }

        // Intenta leer id desde propiedad o campo (case-insensitive)
        public static string? TryGetId(object territory)
        {
            var tp = territory.GetType();

            // Propiedad id/Id
            var pid = tp.GetProperty("id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? tp.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pid != null)
            {
                var v = pid.GetValue(territory);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }

            // Campo id/Id
            var fid = tp.GetField("id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? tp.GetField("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fid != null)
            {
                var v = fid.GetValue(territory);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }

            return null;
        }

        // Intenta leer neighbors desde propiedad o campo (listas/arrays de string)
        public static IEnumerable<string> TryGetNeighbors(object territory)
        {
            var tp = territory.GetType();

            // Propiedad neighbors/Neighbors
            var pn = tp.GetProperty("neighbors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? tp.GetProperty("Neighbors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pn != null)
            {
                foreach (var s in EnumerateStrings(pn.GetValue(territory))) yield return s;
                yield break;
            }

            // Campo neighbors/Neighbors
            var fn = tp.GetField("neighbors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? tp.GetField("Neighbors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fn != null)
            {
                foreach (var s in EnumerateStrings(fn.GetValue(territory))) yield return s;
            }
        }

        private static IEnumerable<string> EnumerateStrings(object? val)
        {
            if (val is string[] arr)
            {
                for (int i = 0; i < arr.Length; i++)
                    if (!string.IsNullOrWhiteSpace(arr[i])) yield return arr[i];
                yield break;
            }

            if (val is System.Collections.IEnumerable en)
            {
                foreach (var x in en)
                    if (x is string s && !string.IsNullOrWhiteSpace(s)) yield return s;
            }
        }

        public static IEnumerable<string> GetTerritoryIds(this Map map)
        {
            foreach (var t in map.GetRawTerritories())
            {
                var id = TryGetId(t);
                if (!string.IsNullOrEmpty(id)) yield return id!;
            }
        }

        public static object? FindTerritoryObject(this Map map, string id)
        {
            foreach (var t in map.GetRawTerritories())
            {
                var tid = TryGetId(t);
                if (tid != null && string.Equals(tid, id, StringComparison.Ordinal))
                    return t;
            }
            return null;
        }

        public static IReadOnlyList<string> NeighborsOf(this Map map, string id)
        {
            var t = map.FindTerritoryObject(id);
            if (t == null) return Array.Empty<string>();
            return TryGetNeighbors(t).ToArray();
        }

        public static bool ContainsTerritoryId(this Map map, string id)
        {
            return map.FindTerritoryObject(id) != null;
        }
    }

    // ========================= Tipos base / DTOs =========================

    public enum Phase
    {
        Reinforcement,
        Attack,
        Fortify
    }

    public sealed class PlayerInfo
    {
        public int Id { get; }
        public string Name { get; }

        public PlayerInfo(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public sealed class TerritoryState
    {
        public string Id { get; }
        public int OwnerId { get; set; }
        public int Troops { get; set; }

        public TerritoryState(string id, int ownerId, int troops)
        {
            Id = id;
            OwnerId = ownerId;
            Troops = troops;
        }
    }

    public sealed class DiceRollResult
    {
        public int[] AttackerDice { get; set; } = Array.Empty<int>();
        public int[] DefenderDice { get; set; } = Array.Empty<int>();
        public int AttackerLosses { get; set; }
        public int DefenderLosses { get; set; }
        public bool TerritoryCaptured { get; set; }
        public string FromId { get; set; } = "";
        public string ToId { get; set; } = "";
    }

    public sealed class GameState
    {
        public Phase Phase { get; set; } = Phase.Reinforcement;
        public int CurrentPlayerId { get; set; }
        public int ReinforcementsRemaining { get; set; }
        public Diccionario<string, TerritoryState> Territories { get; set; } =
            new Diccionario<string, TerritoryState>();
        public Lista<PlayerInfo> Players { get; set; } = new Lista<PlayerInfo>();
        public string? PendingAttackFrom { get; set; }
        public string? PendingAttackTo { get; set; }

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        public static GameState FromJson(string json) => JsonSerializer.Deserialize<GameState>(json)!;
    }

    // ========================= Motor principal =========================

    public sealed class GameEngine
    {
        public Map Map { get; }
        public GameState State { get; private set; } = new GameState();

        private readonly Random rng;

        public GameEngine(Map map, Lista<PlayerInfo> players, int? seed = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            rng = new Random(seed ?? Environment.TickCount);

            if (players == null || players.Count < 2)
                throw new ArgumentException("Se requieren al menos 2 jugadores.", nameof(players));

            State.Players = new Lista<PlayerInfo>();
            for (int i = 0; i < players.Count; i++)
                State.Players.Agregar(players[i]);

            // Recolecta ids de territorios desde Map (con reflexión, ahora soporta propiedades y campos)
            var terrIds = new Lista<string>();
            foreach (var id in Map.GetTerritoryIds())
                terrIds.Agregar(id);
            
            if (terrIds.Count == 0)
                throw new InvalidOperationException("El mapa no contiene territorios legibles (Id/id).");

            Shuffle(terrIds);

            State.Territories = new Diccionario<string, TerritoryState>();
            int pIndex = 0;
            for (int i = 0; i < terrIds.Count; i++)
            {
                var tid = terrIds[i];
                var owner = players[pIndex].Id;
                State.Territories[tid] = new TerritoryState(tid, owner, troops: 1);
                pIndex = (pIndex + 1) % players.Count;
            }

            int[] startPools = { 0, 0, 40, 35, 30, 25, 20 };
            int pool = players.Count < startPools.Length ? startPools[players.Count] : 20;

            var current = players[0].Id;
            State.CurrentPlayerId = current;
            State.Phase = Phase.Reinforcement;
            int owned = CountOwned(current);
            State.ReinforcementsRemaining = Math.Max(3, pool - owned);
        }

        // ========================= Fases =========================

        public void NextPhaseOrTurn()
        {
            switch (State.Phase)
            {
                case Phase.Reinforcement:
                    State.Phase = Phase.Attack;
                    break;

                case Phase.Attack:
                    State.Phase = Phase.Fortify;
                    State.PendingAttackFrom = null;
                    State.PendingAttackTo = null;
                    break;

                case Phase.Fortify:
                    int idx = IndexOfPlayer(State.CurrentPlayerId);
                    int next = (idx + 1) % State.Players.Count;
                    State.CurrentPlayerId = State.Players[next].Id;
                    State.Phase = Phase.Reinforcement;
                    State.PendingAttackFrom = null;
                    State.PendingAttackTo = null;
                    State.ReinforcementsRemaining = CalculateReinforcements(State.CurrentPlayerId);
                    break;
            }
        }

        public int CalculateReinforcements(int playerId)
        {
            int owned = CountOwned(playerId);
            int baseReinf = Math.Max(3, owned / 3);
            int continentBonus = 0; // se puede ampliar con bonus por continentes
            return baseReinf + continentBonus;
        }

        // ========================= Refuerzos =========================

        public bool PlaceReinforcements(string territoryId, int amount, out string error)
        {
            error = "";
            if (State.Phase != Phase.Reinforcement) { error = "No estás en fase de refuerzos."; return false; }
            if (amount <= 0) { error = "Cantidad inválida."; return false; }
            if (!State.Territories.TryGetValue(territoryId, out var t)) { error = "Territorio inválido."; return false; }
            if (t.OwnerId != State.CurrentPlayerId) { error = "Ese territorio no te pertenece."; return false; }
            if (amount > State.ReinforcementsRemaining) { error = "No tienes suficientes refuerzos."; return false; }

            t.Troops += amount;
            State.ReinforcementsRemaining -= amount;
            return true;
        }

        // ========================= Ataque =========================

        public bool SetAttackFrom(string fromId, out string error)
        {
            error = "";
            if (State.Phase != Phase.Attack) { error = "No estás en fase de ataque."; return false; }
            if (!State.Territories.TryGetValue(fromId, out var t)) { error = "Territorio atacante inválido."; return false; }
            if (t.OwnerId != State.CurrentPlayerId) { error = "Debes elegir un territorio propio como atacante."; return false; }
            if (t.Troops < 2) { error = "Necesitas al menos 2 tropas para atacar."; return false; }
            State.PendingAttackFrom = fromId;
            State.PendingAttackTo = null;
            return true;
        }

        public bool SetAttackTo(string toId, out string error)
        {
            error = "";
            if (State.Phase != Phase.Attack) { error = "No estás en fase de ataque."; return false; }
            if (State.PendingAttackFrom == null) { error = "Primero elige un territorio atacante."; return false; }
            if (!State.Territories.TryGetValue(State.PendingAttackFrom, out var att)) { error = "Atacante inválido."; return false; }
            if (!State.Territories.TryGetValue(toId, out var def)) { error = "Defensor inválido."; return false; }
            if (def.OwnerId == State.CurrentPlayerId) { error = "No puedes atacar un territorio propio."; return false; }
            if (!AreAdjacent(State.PendingAttackFrom, toId)) { error = "Los territorios no son adyacentes."; return false; }

            State.PendingAttackTo = toId;
            return true;
        }

        public DiceRollResult? RollAttackOnce(out string error)
        {
            error = "";
            if (State.Phase != Phase.Attack) { error = "No estás en fase de ataque."; return null; }
            if (State.PendingAttackFrom == null || State.PendingAttackTo == null) { error = "Selecciona atacante y defensor."; return null; }

            var from = State.Territories[State.PendingAttackFrom];
            var to = State.Territories[State.PendingAttackTo];

            if (from.OwnerId != State.CurrentPlayerId) { error = "El atacante ya no es tuyo."; return null; }
            if (from.Troops < 2) { error = "No tienes tropas suficientes para atacar."; return null; }
            if (to.OwnerId == State.CurrentPlayerId) { error = "El defensor es tuyo."; return null; }
            if (!AreAdjacent(from.Id, to.Id)) { error = "Ya no son adyacentes."; return null; }

            int attDice = Math.Clamp(from.Troops - 1, 1, 3);
            int defDice = Math.Clamp(to.Troops, 1, 2);

            var att = RollDice(attDice);
            var def = RollDice(defDice);

            Array.Sort(att); Array.Reverse(att);
            Array.Sort(def); Array.Reverse(def);

            int compare = Math.Min(att.Length, def.Length);
            int aLoss = 0, dLoss = 0;
            for (int i = 0; i < compare; i++)
            {
                if (att[i] > def[i]) dLoss++;
                else aLoss++;
            }

            from.Troops -= aLoss;
            to.Troops -= dLoss;

            bool captured = false;
            if (to.Troops <= 0)
            {
                to.OwnerId = from.OwnerId;
                int mustMove = Math.Min(attDice, from.Troops - 1);
                if (mustMove > 0)
                {
                    from.Troops -= mustMove;
                    to.Troops = mustMove;
                }
                else
                {
                    to.Troops = 1;
                    from.Troops = Math.Max(1, from.Troops - 1);
                }
                captured = true;
                State.PendingAttackTo = null;
            }

            return new DiceRollResult
            {
                FromId = from.Id,
                ToId = to.Id,
                AttackerDice = att,
                DefenderDice = def,
                AttackerLosses = aLoss,
                DefenderLosses = dLoss,
                TerritoryCaptured = captured
            };
        }

        // ========================= Fortificación =========================

        public bool FortifyMove(string fromId, string toId, int troops, out string error)
        {
            error = "";
            if (State.Phase != Phase.Fortify) { error = "No estás en fase de fortificación."; return false; }
            if (troops <= 0) { error = "Cantidad inválida."; return false; }
            if (!State.Territories.TryGetValue(fromId, out var from)) { error = "Origen inválido."; return false; }
            if (!State.Territories.TryGetValue(toId, out var to)) { error = "Destino inválido."; return false; }
            if (from.OwnerId != State.CurrentPlayerId || to.OwnerId != State.CurrentPlayerId) { error = "Ambos territorios deben ser tuyos."; return false; }
            if (from.Troops - troops < 1) { error = "Debes dejar al menos 1 tropa en el origen."; return false; }

            if (!AreConnectedByOwnerPath(fromId, toId, State.CurrentPlayerId))
            {
                error = "No hay camino propio entre origen y destino.";
                return false;
            }

            from.Troops -= troops;
            to.Troops += troops;
            return true;
        }

        // ========================= Consultas =========================

        public TerritoryState GetTerritory(string id) => State.Territories[id];

        public bool AreAdjacent(string a, string b)
        {
            if (!Map.ContainsTerritoryId(a) || !Map.ContainsTerritoryId(b)) return false;
            var ns = Map.NeighborsOf(a);
            for (int i = 0; i < ns.Count; i++)
                if (string.Equals(ns[i], b, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public bool AreConnectedByOwnerPath(string fromId, string toId, int ownerId)
        {
            if (fromId == toId) return true;
            if (!Map.ContainsTerritoryId(fromId) || !Map.ContainsTerritoryId(toId)) return false;

            var visited = new Conjunto<string>();
            var q = new Cola<string>();
            q.Encolar(fromId);
            visited.Agregar(fromId);

            while (q.Count > 0)
            {
                var cur = q.Desencolar();
                var ns = Map.NeighborsOf(cur);
                for (int i = 0; i < ns.Count; i++)
                {
                    var n = ns[i];
                    if (visited.Contiene(n)) continue;
                    if (!State.Territories.TryGetValue(n, out var st)) continue;
                    if (st.OwnerId != ownerId) continue;

                    if (n == toId) return true;
                    visited.Agregar(n);
                    q.Encolar(n);
                }
            }
            return false;
        }

        public IReadOnlyList<string> NeighborsOf(string id) => Map.NeighborsOf(id);

        public int CountOwned(int playerId)
        {
            int c = 0;
            State.Territories.ParaCada((key, value) =>
            {
                if (value.OwnerId == playerId) c++;
            });
            return c;
        }

        public bool IsEliminated(int playerId) => CountOwned(playerId) == 0;

        // ========================= Utilidades internas =========================

        private int[] RollDice(int count)
        {
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = rng.Next(1, 7);
            return arr;
        }

        private void Shuffle(Lista<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private int IndexOfPlayer(int playerId)
        {
            for (int i = 0; i < State.Players.Count; i++)
                if (State.Players[i].Id == playerId) return i;
            return -1;
        }
    }
}
