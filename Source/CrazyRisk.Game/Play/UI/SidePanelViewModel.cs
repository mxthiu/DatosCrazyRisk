#nullable enable
using System;
using System.Collections.Generic;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Contrato que implementará el "host" (luego Juego.cs) para ejecutar acciones del motor.
    /// Nos permite testear y mantener desacoplado el ViewModel de la UI real.
    /// </summary>
    public interface IGameUIActions
    {
        // Fase actual del motor
        string PhaseName { get; }

        // --- Refuerzos ---
        int GetReinforcementsRemaining();
        bool TryPlaceReinforcements(string territoryId, int amount, out string error);

        // --- Ataque ---
        string? GetPendingAttackFrom();
        string? GetPendingAttackTo();
        bool TrySetAttackFrom(string territoryId, out string error);
        bool TrySetAttackTo(string territoryId, out string error);
        // Devuelve (attDice[], defDice[], summary) o null si falla
        (int[] att, int[] def, string summary)? TryRollOnce(out string error);

        // --- Fortify ---
        bool TryFortify(string fromId, string toId, int amount, out string error);

        // --- Turno ---
        void NextPhaseOrTurn();

        // --- Info auxiliar ---
        bool IsOwnedByCurrent(string territoryId);
        bool AreAdjacent(string a, string b);
    }

    public enum SideTab { Refuerzos, Ataque, Movimiento, Cartas, Log }

    /// <summary>
    /// ViewModel principal del panel lateral. Conserva estado de cada pestaña
    /// y ofrece métodos que luego serán invocados desde Juego.cs cuando se pulsen botones/áreas.
    /// </summary>
    public sealed class SidePanelViewModel
    {
        private readonly IGameUIActions actions;

        public SideTab CurrentTab { get; private set; } = SideTab.Refuerzos;

        public ReinforcementVM Reinforcement { get; }
        public AttackVM        Attack        { get; }
        public FortifyVM       Fortify       { get; }
        public CardsVM         Cards         { get; }
        public LogVM           Log           { get; }

        public event Action? Changed; // notifica a la UI que hubo cambios (lo leeremos en Draw de Juego.cs)

        public SidePanelViewModel(IGameUIActions actions)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));

            Reinforcement = new ReinforcementVM(actions, RaiseChanged);
            Attack        = new AttackVM(actions, RaiseChanged);
            Fortify       = new FortifyVM(actions, RaiseChanged);
            Cards         = new CardsVM(RaiseChanged);
            Log           = new LogVM(RaiseChanged);
        }

        public void SetTab(SideTab tab)
        {
            if (CurrentTab != tab)
            {
                CurrentTab = tab;
                RaiseChanged();
            }
        }

        public string PhaseText => actions.PhaseName;

        private void RaiseChanged() => Changed?.Invoke();

        // Helpers directos que solemos mostrar en HUD
        public int ReinforcementsRemaining => actions.GetReinforcementsRemaining();
        public string? AttackerId => actions.GetPendingAttackFrom();
        public string? DefenderId => actions.GetPendingAttackTo();
    }

    // -------------------- REFUERZOS --------------------

    public sealed class ReinforcementVM
    {
        private readonly IGameUIActions actions;
        private readonly Action notify;

        public int Step { get; private set; } = 1;

        public ReinforcementVM(IGameUIActions actions, Action notify)
        {
            this.actions = actions;
            this.notify = notify;
        }

        public void IncreaseStep() { Step = Math.Min(20, Step + 1); notify(); }
        public void DecreaseStep() { Step = Math.Max(1,  Step - 1); notify(); }

        public int Remaining => actions.GetReinforcementsRemaining();

        public bool TryPlaceOn(string? selectedTerritory, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(selectedTerritory))
            {
                message = "Selecciona un territorio propio para colocar.";
                return false;
            }
            int place = Math.Min(Step, Remaining);
            if (place <= 0)
            {
                message = "No quedan refuerzos.";
                return false;
            }
            if (!actions.TryPlaceReinforcements(selectedTerritory, place, out var err))
            {
                message = "Refuerzos error: " + err;
                return false;
            }

            message = $"Refuerzos: +{place} en {selectedTerritory}.";
            notify();
            return true;
        }
    }

    // -------------------- ATAQUE --------------------

    public sealed class AttackVM
    {
        private readonly IGameUIActions actions;
        private readonly Action notify;

        public AttackVM(IGameUIActions actions, Action notify)
        {
            this.actions = actions;
            this.notify = notify;
        }

        public string AttackerText => actions.GetPendingAttackFrom() ?? "(elige atacante)";
        public string DefenderText => actions.GetPendingAttackTo()   ?? "(elige defensor)";

        public bool TryPickAttacker(string territoryId, out string message)
        {
            message = "";
            if (!actions.TrySetAttackFrom(territoryId, out var err))
            {
                message = "Atacante error: " + err;
                return false;
            }
            message = "Atacante: " + territoryId;
            notify();
            return true;
        }

        public bool TryPickDefender(string territoryId, out string message)
        {
            message = "";
            if (!actions.TrySetAttackTo(territoryId, out var err))
            {
                message = "Defensor error: " + err;
                return false;
            }
            message = "Defensor: " + territoryId;
            notify();
            return true;
        }

        /// <summary>
        /// Ejecuta la tirada a través del motor. Devuelve un resumen para el Log y los arrays de dados.
        /// </summary>
        public (int[] att, int[] def, string summary)? TryRollOnce(out string message)
        {
            message = "";
            var r = actions.TryRollOnce(out var err);
            if (r == null)
            {
                message = "No se pudo tirar: " + err;
                return null;
            }
            var (att, def, summary) = r.Value;
            message = summary;
            notify();
            return (att, def, summary);
        }
    }

    // -------------------- FORTIFY (Movimiento) --------------------

    public sealed class FortifyVM
    {
        private readonly IGameUIActions actions;
        private readonly Action notify;

        public string? FromId { get; private set; }
        public string? ToId   { get; private set; }
        public int Amount { get; private set; } = 1;

        public FortifyVM(IGameUIActions actions, Action notify)
        {
            this.actions = actions;
            this.notify = notify;
        }

        public void IncreaseAmount() { Amount = Math.Min(99, Amount + 1); notify(); }
        public void DecreaseAmount() { Amount = Math.Max(1,  Amount - 1); notify(); }

        public bool TryPick(string territoryId, out string message)
        {
            message = "";
            // Primero origen (propio). Segundo destino (propio conectado).
            if (FromId == null)
            {
                if (!actions.IsOwnedByCurrent(territoryId))
                {
                    message = "Debes elegir un territorio de origen propio.";
                    return false;
                }
                FromId = territoryId;
                message = "Fortificar origen: " + FromId;
                notify();
                return true;
            }
            else if (ToId == null)
            {
                if (!actions.IsOwnedByCurrent(territoryId))
                {
                    message = "Destino debe ser propio.";
                    return false;
                }
                if (!actions.AreAdjacent(FromId, territoryId))
                {
                    // Nota: en el motor real validamos camino propio con BFS; aquí solo damos pista visual.
                    // La validación completa se hará en TryConfirm().
                }
                ToId = territoryId;
                message = "Fortificar destino: " + ToId;
                notify();
                return true;
            }
            else
            {
                // Si ya hay ambos, reiniciamos con nuevo origen
                FromId = territoryId;
                ToId = null;
                message = "Fortificar origen: " + FromId;
                notify();
                return true;
            }
        }

        public bool TryConfirm(out string message)
        {
            message = "";
            if (FromId == null || ToId == null)
            {
                message = "Elige origen y destino.";
                return false;
            }
            if (!actions.TryFortify(FromId, ToId, Amount, out var err))
            {
                message = "Fortificar error: " + err;
                return false;
            }
            message = $"Fortificar: {FromId} -> {ToId} ({Amount}).";
            // Reset post-movimiento
            FromId = null; ToId = null; Amount = Math.Max(1, Amount);
            notify();
            return true;
        }

        public void Cancel()
        {
            FromId = null; ToId = null;
            notify();
        }
    }

    // -------------------- CARTAS --------------------

    public sealed class CardsVM
    {
        private readonly Action notify;

        // Placeholder para sets de cartas; lo llenaremos cuando integremos el sistema.
        public int CardsCount { get; private set; } = 0;

        public CardsVM(Action notify) { this.notify = notify; }

        public void AddCardForDemo()
        {
            CardsCount++;
            notify();
        }
    }

    // -------------------- LOG --------------------

    public sealed class LogVM
    {
        private readonly Action notify;
        private readonly List<string> lines = new(128);

        public LogVM(Action notify) { this.notify = notify; }

        public IReadOnlyList<string> Lines => lines;

        public void Add(string text)
        {
            lines.Add(text);
            if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
            notify();
        }
    }
}
