#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Controla la fase de Fortify (movimiento de tropas entre territorios propios conectados).
    /// Mantiene selección de origen/destino y valida reglas antes de mover.
    /// </summary>
    public sealed class FortifyService
    {
        private readonly GameEngine engine;

        // Selección actual para la UI (origen/destino)
        public string? SelectedFromId { get; private set; }
        public string? SelectedToId   { get; private set; }

        // -------- Callbacks para integrarse con la UI --------
        public Action<string>? OnInfo { get; set; }
        public Action<string>? OnError { get; set; }
        public Action? OnStateChanged { get; set; }

        public FortifyService(GameEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Limpia selección actual.
        /// </summary>
        public void ClearSelection()
        {
            SelectedFromId = null;
            SelectedToId = null;
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Intenta fijar el origen del movimiento (debe ser territorio propio con >= 2 tropas).
        /// </summary>
        public bool TrySelectFrom(string territoryId)
        {
            if (engine.State.Phase != Phase.Fortify)
            {
                OnError?.Invoke("No estás en fase de fortificación.");
                return false;
            }
            if (!engine.State.Territories.TryGetValue(territoryId, out var t))
            {
                OnError?.Invoke("Territorio inválido.");
                return false;
            }
            if (t.OwnerId != engine.State.CurrentPlayerId)
            {
                OnError?.Invoke("El origen debe ser tuyo.");
                return false;
            }
            if (t.Troops < 2)
            {
                OnError?.Invoke("Debes tener al menos 2 tropas en el origen.");
                return false;
            }

            SelectedFromId = territoryId;
            SelectedToId = null;
            OnInfo?.Invoke($"Fortificar origen: {territoryId}");
            OnStateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Intenta fijar el destino (debe ser tuyo y conectado por camino propio desde el origen).
        /// </summary>
        public bool TrySelectTo(string territoryId)
        {
            if (engine.State.Phase != Phase.Fortify)
            {
                OnError?.Invoke("No estás en fase de fortificación.");
                return false;
            }
            if (SelectedFromId == null)
            {
                OnError?.Invoke("Primero selecciona un territorio de origen.");
                return false;
            }
            if (!engine.State.Territories.TryGetValue(territoryId, out var t))
            {
                OnError?.Invoke("Territorio inválido.");
                return false;
            }
            if (t.OwnerId != engine.State.CurrentPlayerId)
            {
                OnError?.Invoke("El destino debe ser tuyo.");
                return false;
            }
            if (!engine.AreConnectedByOwnerPath(SelectedFromId, territoryId, engine.State.CurrentPlayerId))
            {
                OnError?.Invoke("No hay camino propio entre origen y destino.");
                return false;
            }

            SelectedToId = territoryId;
            OnInfo?.Invoke($"Fortificar destino: {territoryId}");
            OnStateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Lista los territorios del jugador actual que pueden ser origen (>=2 tropas).
        /// </summary>
        public List<string> GetValidFromTerritories()
        {
            var list = new List<string>();
            int pid = engine.State.CurrentPlayerId;
            foreach (var kv in engine.State.Territories)
            {
                var st = kv.Value;
                if (st.OwnerId == pid && st.Troops >= 2)
                    list.Add(kv.Key);
            }
            return list;
        }

        /// <summary>
        /// Devuelve destinos válidos conectados por camino propio desde 'fromId'.
        /// Si fromId es null, devuelve vacío.
        /// </summary>
        public List<string> GetValidToTerritories(string? fromId)
        {
            var list = new List<string>();
            if (fromId == null) return list;

            int pid = engine.State.CurrentPlayerId;
            foreach (var kv in engine.State.Territories)
            {
                if (kv.Key == fromId) continue;
                var st = kv.Value;
                if (st.OwnerId != pid) continue;
                if (engine.AreConnectedByOwnerPath(fromId, kv.Key, pid))
                    list.Add(kv.Key);
            }
            return list;
        }

        /// <summary>
        /// Devuelve (min, max) de tropas movibles desde el origen actual.
        /// min = 1 si hay al menos 2 tropas; max = (tropasOrigen - 1).
        /// </summary>
        public (int min, int max) GetMoveRange()
        {
            if (SelectedFromId == null) return (0, 0);
            var from = engine.State.Territories[SelectedFromId];
            int max = Math.Max(0, from.Troops - 1);
            int min = max > 0 ? 1 : 0;
            return (min, max);
        }

        /// <summary>
        /// Realiza el movimiento 'amount' desde SelectedFromId hacia SelectedToId.
        /// </summary>
        public bool MoveSelected(int amount)
        {
            if (engine.State.Phase != Phase.Fortify)
            {
                OnError?.Invoke("No estás en fase de fortificación.");
                return false;
            }
            if (SelectedFromId == null || SelectedToId == null)
            {
                OnError?.Invoke("Selecciona origen y destino.");
                return false;
            }
            var (min, max) = GetMoveRange();
            if (min == 0 || max == 0)
            {
                OnError?.Invoke("No hay tropas movibles en el origen.");
                return false;
            }
            amount = Math.Clamp(amount, min, max);

            if (engine.FortifyMove(SelectedFromId, SelectedToId, amount, out var err))
            {
                OnInfo?.Invoke($"Fortificar: {SelectedFromId} -> {SelectedToId} ({amount}).");
                // Tras un movimiento, suele terminar la fase (regla clásica: 0 o 1 fortificación por turno).
                // Si prefieres permitir múltiples movimientos, comenta las 2 líneas siguientes.
                ClearSelection();
                OnStateChanged?.Invoke();
                return true;
            }
            else
            {
                OnError?.Invoke(err);
                return false;
            }
        }

        /// <summary>
        /// Movimiento automático básico: selecciona el primer origen válido y el primer destino conectado,
        /// y mueve la mitad redondeada hacia abajo de las tropas movibles (max/2).
        /// Útil para pruebas.
        /// </summary>
        public bool AutoFortify()
        {
            if (engine.State.Phase != Phase.Fortify)
            {
                OnError?.Invoke("No estás en fase de fortificación.");
                return false;
            }

            var fromList = GetValidFromTerritories();
            if (fromList.Count == 0)
            {
                OnInfo?.Invoke("No hay orígenes válidos para fortificar.");
                return false;
            }

            foreach (var f in fromList)
            {
                var tos = GetValidToTerritories(f);
                if (tos.Count == 0) continue;

                SelectedFromId = f;
                SelectedToId = tos[0];
                var (min, max) = GetMoveRange();
                int amount = Math.Max(min, max / 2);
                amount = Math.Clamp(amount, min, max);
                return MoveSelected(amount);
            }

            OnInfo?.Invoke("No hay destinos conectados para fortificar.");
            return false;
        }
    }
}
