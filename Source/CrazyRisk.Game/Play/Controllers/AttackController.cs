#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Orquesta la fase de Ataque para la UI.
    /// Se apoya directamente en CrazyRisk.Core.GameEngine para evitar
    /// dependencias con adapters/servicios que puedan variar entre ramas.
    /// </summary>
    public sealed class AttackController
    {
        private readonly GameEngine _engine;

        // Servicio de selección es opcional; si no lo tienes, pásalo como null.
        private readonly object? _selectionService;
        private readonly Action<string>? _setSelectedCallback;

        /// <param name="engine">Instancia del motor del juego (GameEngine).</param>
        /// <param name="selectionService">
        /// Servicio opcional de selección (si lo tienes). Puedes pasar null.
        /// Si lo pasas y expones un método SetSelected(string), pásalo con setSelectedCallback.
        /// </param>
        /// <param name="setSelectedCallback">
        /// Callback opcional para marcar selección en la UI. Si no tienes SelectionService, deja null.
        /// </param>
        public AttackController(GameEngine engine, object? selectionService = null, Action<string>? setSelectedCallback = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _selectionService = selectionService;
            _setSelectedCallback = setSelectedCallback;
        }

        public bool InAttackPhase => _engine.State.Phase == Phase.Attack;
        public string? PendingFrom => _engine.State.PendingAttackFrom;
        public string? PendingTo   => _engine.State.PendingAttackTo;

        /// <summary>
        /// Selecciona el territorio atacante (debe ser del jugador actual y tener >= 2 tropas).
        /// Limpia el defensor pendiente.
        /// </summary>
        public bool SelectAttacker(string territoryId, out string error)
        {
            error = "";
            var st = _engine.State;
            if (st.Phase != Phase.Attack) { error = "No estás en fase de ataque."; return false; }

            if (!st.Territories.TryGetValue(territoryId, out var t))
            { error = "Territorio atacante inválido."; return false; }

            if (t.OwnerId != st.CurrentPlayerId)
            { error = "Debes elegir un territorio propio como atacante."; return false; }

            if (t.Troops < 2)
            { error = "Necesitas al menos 2 tropas para atacar."; return false; }

            st.PendingAttackFrom = territoryId;
            st.PendingAttackTo = null;

            // Notifica selección (si hay callback/servicio)
            _setSelectedCallback?.Invoke(territoryId);
            return true;
        }

        /// <summary>
        /// Selecciona el territorio defensor (enemigo y adyacente al atacante actual).
        /// </summary>
        public bool SelectDefender(string territoryId, out string error)
        {
            error = "";
            var st = _engine.State;
            if (st.Phase != Phase.Attack) { error = "No estás en fase de ataque."; return false; }
            if (st.PendingAttackFrom == null) { error = "Primero elige un atacante."; return false; }

            if (!st.Territories.TryGetValue(territoryId, out var def))
            { error = "Territorio defensor inválido."; return false; }

            if (def.OwnerId == st.CurrentPlayerId)
            { error = "No puedes atacar un territorio propio."; return false; }

            // Verificamos adyacencia usando el propio motor.
            if (!_engine.AreAdjacent(st.PendingAttackFrom, territoryId))
            { error = "Los territorios no son adyacentes."; return false; }

            st.PendingAttackTo = territoryId;
            _setSelectedCallback?.Invoke(territoryId);
            return true;
        }

        /// <summary>
        /// Ejecuta una tirada (máximo 3 dados atacante, 2 defensor).
        /// Devuelve DiceRollResult si fue válida; si no, error con el motivo.
        /// </summary>
        public bool RollOnce(out DiceRollResult? result, out string error)
        {
            result = null;
            error = "";

            if (_engine.State.Phase != Phase.Attack)
            { error = "No estás en fase de ataque."; return false; }

            var r = _engine.RollAttackOnce(out error);
            if (r == null) return false;

            result = r;
            return true;
        }

        /// <summary>
        /// Resetea la selección de defensor (p.ej. si el usuario cambia de atacante).
        /// </summary>
        public void ClearDefender()
        {
            _engine.State.PendingAttackTo = null;
        }
    }
}
