#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Controla la fase de refuerzos: selección de territorio propio
    /// y colocación de la cantidad configurada (step).
    /// No dibuja ni lee input crudo; recibe eventos de alto nivel desde la UI.
    /// </summary>
    public sealed class ReinforcementController
    {
        private readonly GameEngine _engine;

        /// <summary>Territorio seleccionado para colocar refuerzos.</summary>
        public string? SelectedTerritoryId { get; private set; }

        /// <summary>Cantidad por clic (step) que la UI ajusta con +/-.</summary>
        public int Step { get; private set; } = 1;

        public ReinforcementController(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// La UI avisa que el jugador hizo clic en un territorio.
        /// Si es propio durante la fase de refuerzos, lo toma como destino de refuerzo.
        /// </summary>
        public void HandleTerritoryClick(string territoryId, out string uiMessage)
        {
            uiMessage = string.Empty;

            if (_engine.State.Phase != Phase.Reinforcement)
            {
                uiMessage = "No estás en fase de refuerzos.";
                return;
            }

            if (!_engine.State.Territories.TryGetValue(territoryId, out var t))
            {
                uiMessage = "Territorio inválido.";
                return;
            }

            if (t.OwnerId != _engine.State.CurrentPlayerId)
            {
                uiMessage = "Solo puedes reforzar territorios propios.";
                return;
            }

            SelectedTerritoryId = territoryId;
            uiMessage = $"Seleccionado {territoryId}. Usa (+/-) y pulsa 'Colocar' o 'Confirmar'.";
        }

        /// <summary>
        /// Ajusta el Step con límites razonables.
        /// </summary>
        public void BumpStep(int delta)
        {
            Step = Math.Clamp(Step + delta, 1, 50);
        }

        /// <summary>
        /// Intenta colocar refuerzos en el territorio seleccionado usando el Step actual.
        /// Si quedan menos refuerzos que Step, coloca los que queden.
        /// </summary>
        public bool TryPlace(out string uiMessage)
        {
            uiMessage = string.Empty;

            if (_engine.State.Phase != Phase.Reinforcement)
            {
                uiMessage = "No estás en fase de refuerzos.";
                return false;
            }
            if (SelectedTerritoryId is null)
            {
                uiMessage = "Elige un territorio propio para reforzar.";
                return false;
            }

            int pending = _engine.State.ReinforcementsRemaining;
            if (pending <= 0)
            {
                uiMessage = "No te quedan refuerzos por colocar.";
                return false;
            }

            int amount = Math.Min(Step, pending);
            if (_engine.PlaceReinforcements(SelectedTerritoryId, amount, out string err))
            {
                uiMessage = $"Refuerzos: +{amount} en {SelectedTerritoryId}. Pendientes: {_engine.State.ReinforcementsRemaining}.";
                return true;
            }

            uiMessage = "No se pudo reforzar: " + err;
            return false;
        }

        /// <summary>
        /// Devuelve una vista inmutable con la info para pintar la UI.
        /// </summary>
        public ReinforcementView GetView()
        {
            int current = _engine.State.CurrentPlayerId;
            int pending = _engine.State.ReinforcementsRemaining;

            int troopsOnSelected = 0;
            if (SelectedTerritoryId != null &&
                _engine.State.Territories.TryGetValue(SelectedTerritoryId, out var t))
            {
                troopsOnSelected = t.Troops;
            }

            // Sugerir destinos: por ahora, territorios propios (sin ordenar).
            // La UI puede filtrarlos/ordenarlos si quiere.
            var owned = new List<string>();
            foreach (var kv in _engine.State.Territories)
                if (kv.Value.OwnerId == current)
                    owned.Add(kv.Key);

            return new ReinforcementView(
                currentPlayerId: current,
                selectedTerritoryId: SelectedTerritoryId,
                selectedTerritoryTroops: troopsOnSelected,
                pending: pending,
                step: Step,
                ownTerritories: owned.ToArray()
            );
        }

        /// <summary>
        /// Limpia selección (por ejemplo al pulsar “Cancelar” en la UI).
        /// </summary>
        public void ClearSelection()
        {
            SelectedTerritoryId = null;
        }
    }

    /// <summary>
    /// DTO de sólo lectura para la UI de refuerzos.
    /// </summary>
    public readonly struct ReinforcementView
    {
        public int CurrentPlayerId { get; }
        public string? SelectedTerritoryId { get; }
        public int SelectedTerritoryTroops { get; }
        public int Pending { get; }
        public int Step { get; }
        public string[] OwnTerritories { get; }

        public ReinforcementView(
            int currentPlayerId,
            string? selectedTerritoryId,
            int selectedTerritoryTroops,
            int pending,
            int step,
            string[] ownTerritories)
        {
            CurrentPlayerId = currentPlayerId;
            SelectedTerritoryId = selectedTerritoryId;
            SelectedTerritoryTroops = selectedTerritoryTroops;
            Pending = pending;
            Step = step;
            OwnTerritories = ownTerritories;
        }
    }
}
