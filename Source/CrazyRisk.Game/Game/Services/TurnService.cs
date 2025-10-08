#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Utilidades de turno/fase. Si ya usas engine.NextPhaseOrTurn() desde Juego.cs
    /// no es obligatorio llamar a esto, pero te dejo helpers para calcular refuerzos
    /// y para "enganchar" el inicio de la fase de refuerzos cuando lo necesites.
    /// </summary>
    public sealed class TurnService
    {
        private readonly GameEngine engine;

        public TurnService(GameEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Calcula los refuerzos base: territoriosPropios/3 (redondeo hacia abajo), mínimo 3.
        /// No incluye bonus por continentes.
        /// </summary>
        public int CalculateReinforcements(int playerId)
        {
            var state = engine.State;
            int owned = 0;

            // Contar territorios del jugador
            foreach (var kv in state.Territories)
            {
                if (kv.Value.OwnerId == playerId)
                    owned++;
            }

            int baseReinf = owned / 3;
            if (baseReinf < 3) baseReinf = 3;

            return baseReinf;
        }

        /// <summary>
        /// Llamar al inicio de la fase Reinforcement para setear ReinforcementsRemaining.
        /// Úsalo si controlás manualmente el cambio de fase desde fuera del engine.
        /// (Si tu GameEngine ya lo hace internamente, no es necesario.)
        /// </summary>
        public void BeginReinforcementPhase()
        {
            var state = engine.State;
            if (state.Phase != Phase.Reinforcement) return;

            state.ReinforcementsRemaining = CalculateReinforcements(state.CurrentPlayerId);
        }

        /// <summary>
        /// Azúcar por si querés delegar el avance de fase/turno por acá.
        /// </summary>
        public void NextPhaseOrTurn()
        {
            engine.NextPhaseOrTurn();

            // Si el motor NO setea automáticamente los refuerzos al entrar en Reinforcement,
            // podés destapar esta línea:
            // if (engine.State.Phase == Phase.Reinforcement)
            //     BeginReinforcementPhase();
        }
    }
}