// File: Source/CrazyRisk.Game/GamePlayController.cs
#nullable enable
using System;
using System.Collections.Generic;
using CrazyRisk.Core;

namespace CrazyRiskGame
{
    /// <summary>
    /// Capa de orquestación entre UI (Juego.cs) y CrazyRisk.Core.GameEngine.
    /// No dibuja nada: solo guarda estado de selección y llama al motor.
    /// </summary>
    public sealed class GamePlayController
    {
        public GameEngine Engine { get; }
        public string? SelA { get; private set; } // selección 1 (atacante / origen)
        public string? SelB { get; private set; } // selección 2 (defensor / destino)
        public string LastMessage { get; private set; } = "";
        public DiceRollResult? LastRoll { get; private set; }

        public GamePlayController(Map map, IList<PlayerInfo> players, int? seed = null)
        {
            Engine = new GameEngine(map, players, seed);
        }

        // ---------- Entradas desde la UI ----------

        /// <summary>
        /// Llamar cuando el usuario hace clic sobre un territorio en el tablero.
        /// La semántica depende de la fase.
        /// </summary>
        public void ClickTerritory(string territoryId)
        {
            LastMessage = "";
            LastRoll = null;

            switch (Engine.State.Phase)
            {
                case Phase.Reinforcement:
                    HandleReinforcementClick(territoryId);
                    break;

                case Phase.Attack:
                    HandleAttackClick(territoryId);
                    break;

                case Phase.Fortify:
                    HandleFortifyClick(territoryId);
                    break;
            }
        }

        /// <summary>
        /// Botón: colocar 1 refuerzo en el territorio seleccionado (si procede).
        /// </summary>
        public void PlaceOneReinforcement()
        {
            if (Engine.State.Phase != Phase.Reinforcement)
            {
                LastMessage = "No estás en fase de refuerzos.";
                return;
            }
            if (SelA == null)
            {
                LastMessage = "Selecciona un territorio propio.";
                return;
            }

            if (Engine.PlaceReinforcements(SelA, 1, out var err))
                LastMessage = $"Refuerzo +1 en {SelA}. Restantes: {Engine.State.ReinforcementsRemaining}";
            else
                LastMessage = err;
        }

        /// <summary>
        /// Botón: tirar dados una vez (ataque).
        /// </summary>
        public void RollDiceOnce()
        {
            if (Engine.State.Phase != Phase.Attack)
            {
                LastMessage = "No estás en fase de ataque.";
                return;
            }

            var result = Engine.RollAttackOnce(out var err);
            if (result == null)
            {
                LastMessage = err;
                return;
            }

            LastRoll = result;
            if (result.TerritoryCaptured)
                LastMessage = $"¡Conquistado {result.ToId}! Perdidas Atk:{result.AttackerLosses} Def:{result.DefenderLosses}";
            else
                LastMessage = $"Tirada: Atk[{string.Join(",", result.AttackerDice)}] vs Def[{string.Join(",", result.DefenderDice)}]  Perdidas Atk:{result.AttackerLosses} Def:{result.DefenderLosses}";
        }

        /// <summary>
        /// Botón: mover 1 tropa (fortificación) del A->B si hay camino propio.
        /// </summary>
        public void FortifyMoveOne()
        {
            if (Engine.State.Phase != Phase.Fortify)
            {
                LastMessage = "No estás en fase de fortificación.";
                return;
            }
            if (SelA == null || SelB == null)
            {
                LastMessage = "Elige origen (A) y destino (B) propios.";
                return;
            }

            if (Engine.FortifyMove(SelA, SelB, 1, out var err))
                LastMessage = $"Movida 1 tropa de {SelA} a {SelB}.";
            else
                LastMessage = err;
        }

        /// <summary>
        /// Botón: avanzar fase (Refuerzos->Ataque->Fortify->Siguiente jugador + refuerzos).
        /// </summary>
        public void NextPhaseOrTurn()
        {
            Engine.NextPhaseOrTurn();
            SelA = null; SelB = null; LastRoll = null;

            if (Engine.State.Phase == Phase.Reinforcement)
                LastMessage = $"Turno del Jugador {Engine.State.CurrentPlayerId}. Refuerzos: {Engine.State.ReinforcementsRemaining}";
            else
                LastMessage = $"Fase: {Engine.State.Phase}";
        }

        /// <summary>
        /// Info resumida para overlay en la UI.
        /// </summary>
        public string GetHudSummary()
        {
            return $"Jugador: {Engine.State.CurrentPlayerId} | Fase: {Engine.State.Phase} | Refuerzos: {Engine.State.ReinforcementsRemaining}" +
                   (SelA != null ? $" | A:{SelA}" : "") +
                   (SelB != null ? $" B:{SelB}" : "");
        }

        // ---------- Helpers por fase ----------

        private void HandleReinforcementClick(string tid)
        {
            SelA = tid;
            LastMessage = $"Seleccionado {tid}. Pulsa [+1] para colocar refuerzo (restan {Engine.State.ReinforcementsRemaining}).";
        }

        private void HandleAttackClick(string tid)
        {
            // Si no hay A, intentamos fijar atacante (propio con >=2 tropas).
            if (SelA == null)
            {
                if (!TrySelectAttacker(tid, out var msg))
                    LastMessage = msg;
                else
                    LastMessage = $"Atacante: {SelA}. Ahora elige el defensor adyacente.";
                return;
            }

            // Si hay A y no hay B, intentamos fijar defensor (enemigo y adyacente).
            if (SelB == null)
            {
                if (!TrySelectDefender(tid, out var msg))
                    LastMessage = msg;
                else
                    LastMessage = $"Objetivo: {SelB}. Pulsa [Tirar Dados] para resolver.";
                return;
            }

            // Si ya hay A y B, volver a elegir atacante al clickear propio.
            if (SelA != null && SelB != null)
            {
                // Tocar otro propio con 2+ tropas cambia A.
                if (!TrySelectAttacker(tid, out var msg2))
                {
                    // o intenta cambiar B si es enemigo adyacente
                    if (!TrySelectDefender(tid, out var msg3))
                        LastMessage = msg2 + " / " + msg3;
                    else
                        LastMessage = $"Objetivo: {SelB}. Pulsa [Tirar Dados].";
                }
                else
                {
                    SelB = null;
                    LastMessage = $"Atacante: {SelA}. Ahora elige el defensor.";
                }
            }
        }

        private void HandleFortifyClick(string tid)
        {
            // Selección A (origen) y B (destino), ambos propios con camino
            if (SelA == null)
            {
                if (!Owns(tid))
                {
                    LastMessage = "El origen debe ser tuyo.";
                    return;
                }
                SelA = tid;
                LastMessage = $"Origen: {SelA}. Elige destino propio conectado.";
                return;
            }

            if (SelB == null)
            {
                if (!Owns(tid))
                {
                    LastMessage = "El destino debe ser tuyo.";
                    return;
                }
                if (!Engine.AreConnectedByOwnerPath(SelA, tid, Engine.State.CurrentPlayerId))
                {
                    LastMessage = "No hay camino propio entre origen y destino.";
                    return;
                }
                SelB = tid;
                LastMessage = $"Origen: {SelA}  Destino: {SelB}. Pulsa [Mover 1].";
                return;
            }

            // Si ya hay A y B, clic en otro propio cambia el destino
            if (Owns(tid) && Engine.AreConnectedByOwnerPath(SelA, tid, Engine.State.CurrentPlayerId))
            {
                SelB = tid;
                LastMessage = $"Destino: {SelB}. Pulsa [Mover 1].";
            }
        }

        // ---------- Comprobaciones ----------

        private bool TrySelectAttacker(string tid, out string error)
        {
            error = "";
            if (!Owns(tid)) { error = "Debes elegir un territorio tuyo como atacante."; return false; }
            if (Engine.GetTerritory(tid).Troops < 2) { error = "Necesitas al menos 2 tropas."; return false; }
            if (!Engine.SetAttackFrom(tid, out error)) return false;
            SelA = tid;
            SelB = null;
            return true;
        }

        private bool TrySelectDefender(string tid, out string error)
        {
            error = "";
            if (Owns(tid)) { error = "El objetivo debe ser enemigo."; return false; }
            if (SelA == null) { error = "Primero elige atacante."; return false; }
            if (!Engine.AreAdjacent(SelA, tid)) { error = "No es adyacente al atacante."; return false; }
            if (!Engine.SetAttackTo(tid, out error)) return false;
            SelB = tid;
            return true;
        }

        private bool Owns(string tid)
        {
            var t = Engine.GetTerritory(tid);
            return t.OwnerId == Engine.State.CurrentPlayerId;
        }
    }
}
