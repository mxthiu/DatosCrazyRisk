#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Orquesta la fase de Refuerzos por encima del GameEngine:
    /// - Valida colocaciones
    /// - Expone TryPlace(...) y Undo()
    /// - Emite eventos de UI (info, error, cambios de estado)
    /// </summary>
    public sealed class ReinforcementService
    {
        private readonly GameEngine _engine;

        // Pila simple para poder "Deshacer" la última(s) colocación(es) del turno
        private readonly Stack<(string territoryId, int amount)> _history = new();

        // Eventos para la UI (conecta al UiLog y refrescos en Juego.cs)
        public event Action<string>? OnInfo;
        public event Action<string>? OnError;
        public event Action? OnStateChanged;

        public ReinforcementService(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Refuerzos restantes (passthrough al estado del motor).
        /// </summary>
        public int Remaining => _engine.State.ReinforcementsRemaining;

        /// <summary>
        /// True si se está en fase de refuerzos.
        /// </summary>
        public bool IsActive => _engine.State.Phase == Phase.Reinforcement;

        /// <summary>
        /// Intenta colocar "amount" refuerzos en el territorio dado.
        /// Devuelve true si se aplicó. Si falla, dispara OnError con el motivo.
        /// </summary>
        public bool TryPlace(string territoryId, int amount)
        {
            if (!IsActive)
            {
                RaiseError("No estás en fase de refuerzos.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(territoryId))
            {
                RaiseError("Territorio inválido.");
                return false;
            }

            if (amount <= 0)
            {
                RaiseError("La cantidad debe ser positiva.");
                return false;
            }

            if (amount > Remaining)
            {
                RaiseError("No tienes suficientes refuerzos.");
                return false;
            }

            if (!_engine.State.Territories.TryGetValue(territoryId, out var t))
            {
                RaiseError("Territorio inexistente.");
                return false;
            }

            if (t.OwnerId != _engine.State.CurrentPlayerId)
            {
                RaiseError("Ese territorio no te pertenece.");
                return false;
            }

            if (_engine.PlaceReinforcements(territoryId, amount, out string err))
            {
                _history.Push((territoryId, amount));
                RaiseInfo($"Refuerzos: +{amount} en {territoryId}. Pendientes: {Remaining}");
                RaiseChanged();
                return true;
            }
            else
            {
                RaiseError(err);
                return false;
            }
        }

        /// <summary>
        /// 🔌 Compatibilidad con Juego.cs: mismo efecto que TryPlace(...).
        /// </summary>
        public bool PlaceMany(string territoryId, int amount) => TryPlace(territoryId, amount);

        /// <summary>
        /// Deshace la última colocación realizada en esta fase de refuerzos.
        /// Si no hay historial, no hace nada (devuelve false).
        /// </summary>
        public bool UndoLast()
        {
            if (!IsActive)
            {
                RaiseError("No estás en fase de refuerzos.");
                return false;
            }

            if (_history.Count == 0)
            {
                RaiseError("No hay acciones que deshacer.");
                return false;
            }

            var (territoryId, amount) = _history.Pop();

            var state = _engine.State;
            if (!state.Territories.TryGetValue(territoryId, out var t))
            {
                RaiseError("No se puede deshacer: territorio no encontrado.");
                return false;
            }

            // Garantizamos dejar al menos 1 tropa
            int toRemove = Math.Min(amount, t.Troops - 1);
            if (toRemove <= 0)
            {
                RaiseError("No se puede deshacer esta acción (no hay margen de tropas).");
                return false;
            }

            t.Troops -= toRemove;
            state.ReinforcementsRemaining += toRemove;

            RaiseInfo($"Deshacer: -{toRemove} en {territoryId}. Pendientes: {Remaining}");
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Limpia el historial de la fase actual. Útil al terminar la fase o turno.
        /// </summary>
        public void ClearHistory() => _history.Clear();

        /// <summary>
        /// Atajo para colocar 1, 5 o N en función de lo que permita el remanente.
        /// </summary>
        public bool TryPlaceStep(string territoryId, int step)
        {
            int place = Math.Clamp(step, 1, Remaining);
            return TryPlace(territoryId, place);
        }

        private void RaiseInfo(string msg)  => OnInfo?.Invoke(msg);
        private void RaiseError(string msg) => OnError?.Invoke(msg);
        private void RaiseChanged()         => OnStateChanged?.Invoke();
    }
}