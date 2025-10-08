#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Controlador de fortificación (movimiento de tropas entre territorios propios).
    /// No asume que el motor expone "Neighbors". Por eso admite un delegado opcional de adyacencia.
    /// </summary>
    public sealed class FortifyController
    {
        private readonly GameEngine _engine;
        private readonly Func<string, string, bool>? _areAdjacent; // opcional: valida adyacencia si se provee
        private readonly Action<string>? _log;

        /// <param name="engine">GameEngine actual</param>
        /// <param name="areAdjacent">
        /// Delegado opcional para validar que (fromId,toId) sean adyacentes (p.ej., tu Juego.AreAdjacent).
        /// Si es null, no se valida adyacencia (solo propietario/cantidades).
        /// </param>
        /// <param name="logger">Logger opcional para UI</param>
        public FortifyController(GameEngine engine, Func<string, string, bool>? areAdjacent = null, Action<string>? logger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _areAdjacent = areAdjacent;
            _log = logger;
        }

        /// <summary>
        /// Mueve "amount" tropas desde "fromId" hacia "toId".
        /// Reglas:
        /// - Debe ser fase Fortify.
        /// - Ambos territorios deben pertenecer al jugador actual.
        /// - El origen debe tener > amount (deja mínimo 1).
        /// - Si hay delegado de adyacencia, debe ser true.
        /// </summary>
        public bool Move(string fromId, string toId, int amount, out string error)
        {
            error = string.Empty;

            if (_engine.State.Phase != Phase.Fortify)
            {
                error = "No estás en fase de movimiento.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId) || fromId == toId)
            {
                error = "Par de territorios inválido.";
                return false;
            }

            if (amount <= 0)
            {
                error = "La cantidad a mover debe ser positiva.";
                return false;
            }

            var state = _engine.State;

            if (!state.Territories.TryGetValue(fromId, out var from))
            {
                error = "Territorio origen inexistente.";
                return false;
            }
            if (!state.Territories.TryGetValue(toId, out var to))
            {
                error = "Territorio destino inexistente.";
                return false;
            }

            int player = state.CurrentPlayerId;

            if (from.OwnerId != player || to.OwnerId != player)
            {
                error = "Ambos territorios deben ser tuyos.";
                return false;
            }

            if (from.Troops <= 1)
            {
                error = "El origen debe tener más de 1 tropa.";
                return false;
            }

            // Si tenemos validador de adyacencia, úsalo
            if (_areAdjacent != null && !_areAdjacent(fromId, toId))
            {
                error = "El destino no es adyacente al origen.";
                return false;
            }

            // Asegurar que no dejo el origen en 0
            int maxMovibles = Math.Max(0, from.Troops - 1);
            int move = Math.Min(amount, maxMovibles);
            if (move <= 0)
            {
                error = "No hay tropas suficientes para mover (debe quedar al menos 1 en el origen).";
                return false;
            }

            from.Troops -= move;
            to.Troops   += move;

            _log?.Invoke($"Fortificar: {fromId} -> {toId} ({move}).");
            return true;
        }
    }
}