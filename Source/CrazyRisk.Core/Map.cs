using System;
using System.Text.Json;

namespace CrazyRisk.Core
{
    // ---------- Modelo ----------
    public sealed class Territory
    {
        public readonly string Id;
        public readonly int Index;
        public readonly int[] Neighbors; // índices

        public Territory(string id, int index, int[] neighbors)
        {
            Id = id;
            Index = index;
            Neighbors = neighbors;
        }
    }

    public sealed class Map
    {
        public readonly Territory[] Territories;

        public Map(Territory[] territories)
        {
            Territories = territories;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Territories.Length; i++)
                if (Territories[i].Id == id) return i;
            return -1;
        }

        public bool AreNeighbors(int a, int b)
        {
            if ((uint)a >= (uint)Territories.Length) return false;
            if ((uint)b >= (uint)Territories.Length) return false;
            var neigh = Territories[a].Neighbors;
            for (int i = 0; i < neigh.Length; i++)
                if (neigh[i] == b) return true;
            return false;
        }
    }

    // ---------- DTOs JSON ----------
    internal sealed class TerritoryDTO
    {
        public string id { get; set; } = "";
        public string[] neighbors { get; set; } = Array.Empty<string>();
    }

    internal sealed class TerritoriesRootDTO
    {
        public TerritoryDTO[] territories { get; set; } = Array.Empty<TerritoryDTO>();
    }

    // ---------- Loader ----------
    public static class MapLoader
    {
        public static Map FromJson(string jsonText)
        {
            var root = JsonSerializer.Deserialize<TerritoriesRootDTO>(jsonText);
            if (root == null || root.territories == null || root.territories.Length == 0)
                throw new InvalidOperationException("JSON inválido o vacío.");

            var dtos = root.territories;
            int n = dtos.Length;

            // construir tabla id -> índice
            var ids = new string[n];
            for (int i = 0; i < n; i++) ids[i] = dtos[i].id;

            int IdxOf(string id)
            {
                for (int k = 0; k < n; k++)
                    if (ids[k] == id) return k;
                return -1;
            }

            var terrs = new Territory[n];
            for (int i = 0; i < n; i++)
            {
                var dto = dtos[i];
                // mapear vecinos (por id) a índices válidos
                var tmp = new int[dto.neighbors.Length];
                int count = 0;
                for (int j = 0; j < dto.neighbors.Length; j++)
                {
                    int idx = IdxOf(dto.neighbors[j]);
                    if (idx >= 0 && idx != i)
                    {
                        tmp[count++] = idx;
                    }
                }
                var neighbors = new int[count];
                for (int t = 0; t < count; t++) neighbors[t] = tmp[t];

                terrs[i] = new Territory(dto.id, i, neighbors);
            }

            return new Map(terrs);
        }
    }
}
