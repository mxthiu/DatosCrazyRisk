#nullable enable
using System;
using CrazyRiskGame.Play.UI;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Orquesta la selección de territorios según la fase.
    /// No dibuja ni conoce MonoGame; devuelve mensajes para UI/Log.
    /// </summary>
    public sealed class MapSelectionController
    {
        private readonly IGameUIActions actions;

        public MapSelectionController(IGameUIActions actions)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        // Selección "manual" para fases no-ataque (Refuerzos/Fortify)
        public string? SelectedFromId { get; private set; }
        public string? SelectedToId   { get; private set; }

        /// <summary>
        /// Resetea selección al cambiar de fase o al cancelar.
        /// </summary>
        public void ClearSelection()
        {
            SelectedFromId = null;
            SelectedToId = null;
        }

        /// <summary>
        /// Llamar cuando el usuario clickea un territorio en el mapa.
        /// Devuelve una línea de log descriptiva (o null si no hay nada que decir).
        /// </summary>
        public string? HandleTerritoryClick(string territoryId)
        {
            if (string.IsNullOrWhiteSpace(territoryId))
                return null;

            string phase = actions.PhaseName;

            switch (phase)
            {
                case "Reinforcement":
                    return HandleReinforcementClick(territoryId);

                case "Attack":
                    return HandleAttackClick(territoryId);

                case "Fortify":
                    return HandleFortifyClick(territoryId);

                default:
                    // Fases futuras o gestión
                    return $"({phase}) Selección: {territoryId}";
            }
        }

        private string? HandleReinforcementClick(string territoryId)
        {
            // En refuerzos, solo seleccionamos un territorio propio (colocación real la hace UI/acciones).
            if (actions.IsOwnedByCurrent(territoryId))
            {
                SelectedFromId = territoryId;
                SelectedToId = null;
                return $"Refuerzos: seleccionado {territoryId}. Usa el botón para colocar.";
            }
            else
            {
                return "Ese territorio no te pertenece para colocar refuerzos.";
            }
        }

        private string? HandleAttackClick(string territoryId)
        {
            // Ataque: usamos el motor via IGameUIActions (PendingAttackFrom / PendingAttackTo)
            string err;
            var currentFrom = actions.GetPendingAttackFrom();
            var currentTo   = actions.GetPendingAttackTo();

            if (currentFrom == null)
            {
                if (actions.TrySetAttackFrom(territoryId, out err))
                {
                    // Limpia selección manual (no se usa en ataque)
                    SelectedFromId = null; SelectedToId = null;
                    return $"Atacante: {territoryId}";
                }
                return $"Atacante error: {err}";
            }

            if (currentTo == null)
            {
                if (actions.TrySetAttackTo(territoryId, out err))
                {
                    return $"Defensor: {territoryId}";
                }
                return $"Defensor error: {err}";
            }

            // Si ya había ambos, un clic elige nuevo atacante
            if (actions.TrySetAttackFrom(territoryId, out err))
            {
                return $"Atacante: {territoryId} (selecciona defensor)";
            }
            return $"Atacante error: {err}";
        }

        private string? HandleFortifyClick(string territoryId)
        {
            // Fortify: selección manual Origen -> Destino (validación real al confirmar movimiento)
            if (SelectedFromId == null)
            {
                if (actions.IsOwnedByCurrent(territoryId))
                {
                    SelectedFromId = territoryId;
                    SelectedToId = null;
                    return $"Fortificar origen: {territoryId}";
                }
                return "Debes elegir un territorio propio como origen.";
            }
            else if (SelectedToId == null)
            {
                if (actions.IsOwnedByCurrent(territoryId))
                {
                    SelectedToId = territoryId;
                    return $"Fortificar destino: {territoryId}. Ajusta cantidad y confirma.";
                }
                // Si no es propio, reseteamos el origen para evitar confusiones
                SelectedFromId = null;
                return "Destino inválido (debe ser propio). Selección reiniciada.";
            }
            else
            {
                // Si ya hay origen/destino, un clic re-selecciona nuevo origen
                if (actions.IsOwnedByCurrent(territoryId))
                {
                    SelectedFromId = territoryId;
                    SelectedToId = null;
                    return $"Fortificar origen: {territoryId}";
                }
                SelectedFromId = null;
                SelectedToId = null;
                return "Selección reiniciada.";
            }
        }
    }
}
