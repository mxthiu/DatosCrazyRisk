#nullable enable
using System;
using CrazyRiskGame.Play.UI;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// ViewModel de la barra superior: fase, refuerzos y selección actual.
    /// Solo expone texto; Juego.cs decide cómo dibujarlo.
    /// </summary>
    public sealed class PlayerPanelViewModel
    {
        private readonly IGameUIActions actions;

        public PlayerPanelViewModel(IGameUIActions actions)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        // Labels listos para pintar
        public string PhaseText { get; private set; } = "Fase: --";
        public string ReinforcementsText { get; private set; } = "Refuerzos: --";
        public string SelectionText { get; private set; } = "Selección: (ninguna)";

        /// <summary>
        /// Actualiza los textos con el estado actual + la selección del mapa
        /// (por ejemplo, atacante/defensor en Ataque o origen/destino en Fortify).
        /// </summary>
        public void Refresh(string? selectedFromId, string? selectedToId)
        {
            // Fase
            PhaseText = $"Fase: {actions.PhaseName}";

            // Refuerzos visibles sólo cuando aplique (si no, mostramos "--")
            int r = actions.GetReinforcementsRemaining();
            ReinforcementsText = r >= 0 ? $"Refuerzos: {r}" : "Refuerzos: --";

            // Selección contextual (si estamos en ataque, usamos Pending del engine)
            var pendA = actions.GetPendingAttackFrom();
            var pendD = actions.GetPendingAttackTo();

            if (pendA != null || pendD != null)
            {
                SelectionText = $"Selección: {pendA ?? "(elige atacante)"} -> {pendD ?? "(elige defensor)"}";
            }
            else
            {
                // fallback a selección manual (refuerzos/fortify)
                string from = selectedFromId ?? "(ninguno)";
                string to   = selectedToId   ?? "(ninguno)";
                SelectionText = selectedToId == null
                    ? $"Selección: {from}"
                    : $"Selección: {from} -> {to}";
            }
        }
    }
}
