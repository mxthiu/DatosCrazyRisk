#nullable enable
using System;
using CrazyRisk.Core;
using CrazyRiskGame.Play.UI;

namespace CrazyRiskGame.Play.Adapters
{
    /// <summary>
    /// Adaptador que implementa IGameUIActions envolviendo un GameEngine real.
    /// No depende de MonoGame ni de Juego.cs, así que puedes testearlo aparte.
    /// </summary>
    public sealed class EngineActionsAdapter : IGameUIActions
    {
        private readonly GameEngine engine;

        public EngineActionsAdapter(GameEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        // --------------------- Estado / Fase ---------------------

        public string PhaseName => engine.State.Phase.ToString();

        // --------------------- Refuerzos -------------------------

        public int GetReinforcementsRemaining() => engine.State.ReinforcementsRemaining;

        public bool TryPlaceReinforcements(string territoryId, int amount, out string error)
        {
            return engine.PlaceReinforcements(territoryId, amount, out error);
        }

        // --------------------- Ataque ----------------------------

        public string? GetPendingAttackFrom() => engine.State.PendingAttackFrom;
        public string? GetPendingAttackTo()   => engine.State.PendingAttackTo;

        public bool TrySetAttackFrom(string territoryId, out string error)
        {
            return engine.SetAttackFrom(territoryId, out error);
        }

        public bool TrySetAttackTo(string territoryId, out string error)
        {
            return engine.SetAttackTo(territoryId, out error);
        }

        /// <summary>
        /// Ejecuta una tirada de ataque en el motor y devuelve:
        /// (dados atacante, dados defensor, texto resumen para Log).
        /// </summary>
        public (int[] att, int[] def, string summary)? TryRollOnce(out string error)
        {
            var r = engine.RollAttackOnce(out error);
            if (r == null) return null;

            // Construimos un resumen legible para el Log/UI:
            // Ej: "A[6,5,2] vs D[5,3]  Perdidas A:1 D:1 CAPTURADO  (ESPANA -> FRANCIA)"
            string attStr = string.Join(",", r.AttackerDice);
            string defStr = string.Join(",", r.DefenderDice);
            string captured = r.TerritoryCaptured ? " CAPTURADO" : "";
            string summary =
                $"A[{attStr}] vs D[{defStr}]  Perdidas A:{r.AttackerLosses} D:{r.DefenderLosses}{captured}  ({r.FromId} -> {r.ToId})";

            return (r.AttackerDice, r.DefenderDice, summary);
        }

        // --------------------- Fortify ---------------------------

        public bool TryFortify(string fromId, string toId, int amount, out string error)
        {
            return engine.FortifyMove(fromId, toId, amount, out error);
        }

        // --------------------- Turno -----------------------------

        public void NextPhaseOrTurn() => engine.NextPhaseOrTurn();

        // --------------------- Info auxiliar ---------------------

        public bool IsOwnedByCurrent(string territoryId)
        {
            if (!engine.State.Territories.TryGetValue(territoryId, out var st)) return false;
            return st.OwnerId == engine.State.CurrentPlayerId;
        }

        public bool AreAdjacent(string a, string b) => engine.AreAdjacent(a, b);
    }
}
