#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Reglas de cambio de fase y resumen de turno.
    /// No toca UI ni dibuja: solo decide si puedes terminar la fase y lleva contadores simples.
    /// </summary>
    public sealed class TurnService
    {
        private readonly GameEngine engine;
        private readonly bool singleFortifyPerTurn;
        private bool fortifyUsedThisTurn;

        public TurnService(GameEngine engine, bool singleFortifyPerTurn = true)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.singleFortifyPerTurn = singleFortifyPerTurn;
        }

        /// <summary>
        /// Llamar cuando entras a una fase (ej. justo al hacer NextPhaseOrTurn o al iniciar la partida).
        /// </summary>
        public void OnPhaseEnter(Phase phase)
        {
            if (phase == Phase.Fortify)
                fortifyUsedThisTurn = false;
        }

        /// <summary>
        /// Llamar si quieres enganchar side-effects al salir de la fase (opcional).
        /// </summary>
        public void OnPhaseExit(Phase phase)
        {
            // Por ahora no hacemos nada, pero queda el hook por si agregas sonidos/logs.
        }

        /// <summary>
        /// Notifica que se realizó una fortificación válida (para la política "una por turno").
        /// Debe llamarse desde el lugar donde haces FortifyMove= true en tu controlador.
        /// </summary>
        public void OnFortifyPerformed()
        {
            fortifyUsedThisTurn = true;
        }

        /// <summary>
        /// ¿Se puede terminar la fase actual? (Para habilitar/deshabilitar "Siguiente")
        /// </summary>
        public bool CanEndCurrentPhase(out string reasonIfNot)
        {
            switch (engine.State.Phase)
            {
                case Phase.Reinforcement:
                    return CanEndReinforcement(out reasonIfNot);
                case Phase.Attack:
                    return CanEndAttack(out reasonIfNot);
                case Phase.Fortify:
                    return CanEndFortify(out reasonIfNot);
                default:
                    reasonIfNot = "";
                    return true;
            }
        }

        public bool CanEndReinforcement(out string reasonIfNot)
        {
            if (engine.State.ReinforcementsRemaining > 0)
            {
                reasonIfNot = "Aún te quedan refuerzos por colocar.";
                return false;
            }
            reasonIfNot = "";
            return true;
        }

        public bool CanEndAttack(out string reasonIfNot)
        {
            // Si no hay ningún ataque posible, puedes pasar de fase.
            if (!HasAnyPossibleAttack(engine.State.CurrentPlayerId))
            {
                reasonIfNot = "";
                return true;
            }

            // Hay ataques posibles; igual permitimos pasar si quieres hacerlo voluntario.
            // Si deseas forzar a atacar al menos una vez, cambia esto a false y da un motivo.
            reasonIfNot = "";
            return true;
        }

        public bool CanEndFortify(out string reasonIfNot)
        {
            if (!singleFortifyPerTurn)
            {
                reasonIfNot = "";
                return true;
            }

            // Política: permitir terminar fortify solo tras al menos un movimiento (o si no hay movimiento posible).
            if (fortifyUsedThisTurn)
            {
                reasonIfNot = "";
                return true;
            }

            // Si no hay ningún par de territorios conectados por camino propio con >1 tropa en origen,
            // dejamos terminar aunque no haya movimiento.
            if (!HasAnyPossibleFortify(engine.State.CurrentPlayerId))
            {
                reasonIfNot = "";
                return true;
            }

            reasonIfNot = "Puedes realizar una fortificación (una por turno).";
            return false;
        }

        /// <summary>
        /// ¿Queda al menos un ataque posible para el jugador actual?
        /// </summary>
        public bool HasAnyPossibleAttack(int playerId)
        {
            foreach (var kv in engine.State.Territories)
            {
                var t = kv.Value;
                if (t.OwnerId != playerId) continue;
                if (t.Troops < 2) continue;

                var neighbors = engine.NeighborsOf(t.Id);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    var nid = neighbors[i];
                    if (!engine.State.Territories.TryGetValue(nid, out var nState)) continue;
                    if (nState.OwnerId != playerId)
                        return true; // hay enemigo adyacente
                }
            }
            return false;
        }

        /// <summary>
        /// ¿Existe al menos un movimiento de fortificación posible?
        /// (Origen propio con tropas >1 y camino propio a algún destino propio).
        /// </summary>
        public bool HasAnyPossibleFortify(int playerId)
        {
            // Búsqueda conservadora: si un territorio propio tiene >1 tropa y existe otro propio conectado, ya es posible.
            // (No verificamos todos los pares en detalle por performance; basta una evidencia positiva.)
            var owned = new List<string>(engine.State.Territories.Count);
            foreach (var kv in engine.State.Territories)
                if (kv.Value.OwnerId == playerId) owned.Add(kv.Key);

            foreach (var fromId in owned)
            {
                var from = engine.State.Territories[fromId];
                if (from.Troops <= 1) continue;

                // ¿Hay algún destino propio distinto conectado?
                for (int i = 0; i < owned.Count; i++)
                {
                    var toId = owned[i];
                    if (toId == fromId) continue;
                    if (engine.AreConnectedByOwnerPath(fromId, toId, playerId))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Devuelve un texto corto para UI indicando el "estado de finalización" de la fase,
        /// útil para un tooltip del botón "Siguiente".
        /// </summary>
        public string EndPhaseHint()
        {
            if (!CanEndCurrentPhase(out var whyNot) && !string.IsNullOrWhiteSpace(whyNot))
                return whyNot;

            return engine.State.Phase switch
            {
                Phase.Reinforcement => "Listo: no quedan refuerzos pendientes.",
                Phase.Attack        => "Puedes pasar a fortificación cuando quieras.",
                Phase.Fortify       => singleFortifyPerTurn
                    ? (fortifyUsedThisTurn ? "Listo: ya hiciste tu fortificación." : "Puedes realizar una fortificación (opcional).")
                    : "Puedes terminar la fase de fortificación.",
                _ => ""
            };
        }
    }
}
