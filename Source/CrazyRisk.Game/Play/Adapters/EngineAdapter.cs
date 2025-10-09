#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Adapters
{
    /// <summary>
    /// Envoltura del GameEngine: métodos con salida (ok, error),
    /// y eventos simples para log/cambios.
    /// </summary>
    public sealed class EngineAdapter
    {
        public GameEngine Engine { get; }

        // Eventos simples (opcionalmente los puedes ignorar si no los necesitas)
        public event Action<string>? OnLog;
        public event Action? OnStateChanged;

        public EngineAdapter(GameEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        // ======== Lecturas rápidas del estado ========
        public Phase Phase => Engine.State.Phase;
        public int CurrentPlayerId => Engine.State.CurrentPlayerId;
        public int ReinforcementsRemaining => Engine.State.ReinforcementsRemaining;
        public CrazyRisk.Core.DataStructures.Diccionario<string, TerritoryState> Territories => Engine.State.Territories;

        public bool TryPlaceReinforcements(string territoryId, int amount, out string error)
        {
            var ok = Engine.PlaceReinforcements(territoryId, amount, out error);
            if (ok)
            {
                OnLog?.Invoke($"Refuerzos: +{amount} en {territoryId}.");
                OnStateChanged?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                OnLog?.Invoke($"[Refuerzos] {error}");
            }
            return ok;
        }

        public bool TrySetAttackFrom(string id, out string error)
        {
            var ok = Engine.SetAttackFrom(id, out error);
            if (ok)
            {
                OnLog?.Invoke($"Atacante: {id}");
                OnStateChanged?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                OnLog?.Invoke($"[Atacante] {error}");
            }
            return ok;
        }

        public bool TrySetAttackTo(string id, out string error)
        {
            var ok = Engine.SetAttackTo(id, out error);
            if (ok)
            {
                OnLog?.Invoke($"Defensor: {id}");
                OnStateChanged?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                OnLog?.Invoke($"[Defensor] {error}");
            }
            return ok;
        }

        public DiceRollResult? TryRollAttackOnce(out string error)
        {
            var r = Engine.RollAttackOnce(out error);
            if (r != null)
            {
                OnLog?.Invoke($"Tiro: A[{string.Join(",", r.AttackerDice)}] vs D[{string.Join(",", r.DefenderDice)}]  Pérdidas A:{r.AttackerLosses} D:{r.DefenderLosses}" + (r.TerritoryCaptured ? " CAPTURADO" : ""));
                OnStateChanged?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                OnLog?.Invoke($"[Dados] {error}");
            }
            return r;
        }

        public bool TryFortify(string fromId, string toId, int troops, out string error)
        {
            var ok = Engine.FortifyMove(fromId, toId, troops, out error);
            if (ok)
            {
                OnLog?.Invoke($"Fortificar: {fromId} -> {toId} ({troops}).");
                OnStateChanged?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                OnLog?.Invoke($"[Fortify] {error}");
            }
            return ok;
        }

        public void NextPhaseOrTurn()
        {
            Engine.NextPhaseOrTurn();
            OnLog?.Invoke($"Siguiente: {Engine.State.Phase}");
            OnStateChanged?.Invoke();
        }

        // Utilidades de consulta
        public bool AreAdjacent(string a, string b) => Engine.AreAdjacent(a, b);
        public bool AreConnectedByOwnerPath(string fromId, string toId, int ownerId) => Engine.AreConnectedByOwnerPath(fromId, toId, ownerId);

        public TerritoryState GetTerritory(string id) => Engine.GetTerritory(id);
        public IReadOnlyList<string> NeighborsOf(string id) => Engine.NeighborsOf(id);
    }
}
