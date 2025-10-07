#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Servicio de selección de territorios dependiente de la fase.
    /// No dibuja ni conoce la UI. Responde a clics (por id) y coordina
    /// con GameEngine las elecciones válidas.
    /// </summary>
    public sealed class SelectionService
    {
        private readonly GameEngine engine;

        // ---- Callbacks para UI (opcionales) ----
        public Action<string>? OnInfo { get; set; }
        public Action<string>? OnError { get; set; }
        public Action<string>? OnHint { get; set; }

        // ---- Estado de selección por fase ----
        // Refuerzos: último territorio propio seleccionado (para “colocar aquí”).
        public string? SelectedForReinforcement { get; private set; }

        // Ataque usa el estado interno del engine (PendingAttackFrom/To) como verdad única.
        public string? AttackFrom => engine.State.PendingAttackFrom;
        public string? AttackTo   => engine.State.PendingAttackTo;

        // Fortify: seleccionamos localmente y luego se ejecuta FortifyMove.
        public string? FortifyFrom { get; private set; }
        public string? FortifyTo   { get; private set; }

        public SelectionService(GameEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Llamar cuando cambie la fase (o al iniciar) para limpiar estado correspondiente.
        /// </summary>
        public void ResetForPhase(Phase phase)
        {
            switch (phase)
            {
                case Phase.Reinforcement:
                    SelectedForReinforcement = null;
                    // No toco AttackFrom/To: las maneja el engine cuando cambia de fase
                    FortifyFrom = null; FortifyTo = null;
                    OnHint?.Invoke("Coloca refuerzos solo en territorios propios.");
                    break;

                case Phase.Attack:
                    SelectedForReinforcement = null;
                    FortifyFrom = null; FortifyTo = null;
                    // El engine deja PendingAttackFrom/To en null al entrar desde NextPhaseOrTurn
                    OnHint?.Invoke("Elige un territorio propio con ≥2 tropas y luego un vecino enemigo.");
                    break;

                case Phase.Fortify:
                    SelectedForReinforcement = null;
                    engine.State.PendingAttackFrom = null;
                    engine.State.PendingAttackTo = null;
                    FortifyFrom = null; FortifyTo = null;
                    OnHint?.Invoke("Elige origen propio (>1 tropa) y destino propio conectado.");
                    break;
            }
        }

        /// <summary>
        /// Maneja un click sobre el mapa según la fase actual.
        /// </summary>
        public void HandleClick(string territoryId)
        {
            switch (engine.State.Phase)
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

        // ================= REINFORCEMENT =================
        private void HandleReinforcementClick(string id)
        {
            if (!engine.State.Territories.TryGetValue(id, out var t))
            {
                OnError?.Invoke("Territorio inválido.");
                return;
            }
            if (t.OwnerId != engine.State.CurrentPlayerId)
            {
                OnError?.Invoke("Solo puedes reforzar territorios propios.");
                return;
            }

            SelectedForReinforcement = id;
            OnInfo?.Invoke($"Seleccionado para refuerzos: {id}. Pendientes: {engine.State.ReinforcementsRemaining}.");
        }

        // ================= ATTACK =================
        private void HandleAttackClick(string id)
        {
            if (engine.State.PendingAttackFrom == null)
            {
                // Elegir atacante
                if (engine.SetAttackFrom(id, out string errA))
                {
                    OnInfo?.Invoke($"Atacante: {id}. Ahora elige un defensor adyacente enemigo.");
                }
                else
                {
                    OnError?.Invoke(errA);
                }
                return;
            }

            if (engine.State.PendingAttackTo == null)
            {
                // Elegir defensor
                if (engine.SetAttackTo(id, out string errD))
                {
                    OnInfo?.Invoke($"Defensor: {id}. Pulsa 'Lanzar dados' para resolver.");
                }
                else
                {
                    OnError?.Invoke(errD);
                }
                return;
            }

            // Si ya hay par completo y el usuario hace click en otro territorio:
            // Interpretamos que quiere cambiar el atacante.
            if (engine.SetAttackFrom(id, out string errA2))
            {
                engine.State.PendingAttackTo = null;
                OnInfo?.Invoke($"Atacante: {id}. Vuelve a elegir defensor.");
            }
            else
            {
                OnError?.Invoke(errA2);
            }
        }

        // ================= FORTIFY =================
        private void HandleFortifyClick(string id)
        {
            if (!engine.State.Territories.TryGetValue(id, out var t))
            {
                OnError?.Invoke("Territorio inválido.");
                return;
            }

            // Primer click: origen propio con >1 tropa
            if (FortifyFrom == null)
            {
                if (t.OwnerId != engine.State.CurrentPlayerId)
                {
                    OnError?.Invoke("El origen debe ser propio.");
                    return;
                }
                if (t.Troops <= 1)
                {
                    OnError?.Invoke("El origen debe tener más de 1 tropa.");
                    return;
                }

                FortifyFrom = id;
                FortifyTo = null;
                OnInfo?.Invoke($"Fortificar origen: {id}. Ahora elige destino propio conectado.");
                return;
            }

            // Segundo click: destino propio conectado
            if (t.OwnerId != engine.State.CurrentPlayerId)
            {
                OnError?.Invoke("El destino debe ser propio.");
                return;
            }
            if (id == FortifyFrom)
            {
                // Permite “des-seleccionar”
                FortifyFrom = null;
                FortifyTo = null;
                OnInfo?.Invoke("Origen deseleccionado.");
                return;
            }

            if (!engine.AreConnectedByOwnerPath(FortifyFrom, id, engine.State.CurrentPlayerId))
            {
                OnError?.Invoke("No hay un camino propio entre origen y destino.");
                return;
            }

            FortifyTo = id;
            OnInfo?.Invoke($"Fortificar: {FortifyFrom} -> {FortifyTo}. Ajusta cantidad y confirma movimiento.");
        }

        /// <summary>
        /// Intenta ejecutar el movimiento de fortificación con la cantidad solicitada.
        /// (No llama OnFortifyPerformed del TurnService; eso lo hace tu controlador/UI).
        /// </summary>
        public bool TryExecuteFortify(int amount, out string msg)
        {
            msg = "";
            if (engine.State.Phase != Phase.Fortify)
            {
                msg = "No estás en fase de fortificación.";
                return false;
            }
            if (FortifyFrom == null || FortifyTo == null)
            {
                msg = "Selecciona origen y destino válidos.";
                return false;
            }

            if (engine.FortifyMove(FortifyFrom, FortifyTo, amount, out string err))
            {
                OnInfo?.Invoke($"Fortificación realizada: {FortifyFrom} -> {FortifyTo} ({amount}).");
                // Limpia selección para permitir otra (o terminar fase).
                FortifyFrom = null;
                FortifyTo = null;
                return true;
            }
            else
            {
                OnError?.Invoke(err);
                return false;
            }
        }

        // ================= Utilidades públicas =================

        public bool IsOwn(string id)
        {
            return engine.State.Territories.TryGetValue(id, out var t)
                   && t.OwnerId == engine.State.CurrentPlayerId;
        }

        public int TroopsOf(string id)
        {
            return engine.State.Territories.TryGetValue(id, out var t) ? t.Troops : 0;
        }

        public bool IsAdjacentEnemy(string fromId, string toId)
        {
            if (!engine.AreAdjacent(fromId, toId)) return false;
            if (!engine.State.Territories.TryGetValue(toId, out var t)) return false;
            return t.OwnerId != engine.State.CurrentPlayerId;
        }

        public (string? primary, string? secondary) CurrentHighlights()
        {
            // Útil si quieres resaltar en UI: refuerzo usa SelectedForReinforcement;
            // ataque usa pending del engine; fortify usa local.
            return engine.State.Phase switch
            {
                Phase.Reinforcement => (SelectedForReinforcement, null),
                Phase.Attack        => (AttackFrom, AttackTo),
                Phase.Fortify       => (FortifyFrom, FortifyTo),
                _                   => (null, null)
            };
        }

        public void ClearAll()
        {
            SelectedForReinforcement = null;
            engine.State.PendingAttackFrom = null;
            engine.State.PendingAttackTo   = null;
            FortifyFrom = null;
            FortifyTo   = null;
        }
    }
}
