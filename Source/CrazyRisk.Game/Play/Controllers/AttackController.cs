#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Resuelve un ataque estilo Risk sobre el GameEngine.
    /// - Adyacencia y selección se validan en la UI (Juego.cs).
    /// - Aplica pérdidas y captura territorio si llega a 0 tropas.
    /// - Devuelve CrazyRisk.Core.DiceRollResult (sin tipos duplicados).
    /// </summary>
    public sealed class AttackController
    {
        private readonly GameEngine _engine;
        private readonly Random _rng = new();

        private string? _attackerId;
        private string? _defenderId;

        private readonly Action<string>? _log;

        public AttackController(GameEngine engine, object? _unused, Action<string>? logger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _log    = logger;
        }

        public void SetPair(string attackerId, string defenderId)
        {
            _attackerId = attackerId;
            _defenderId = defenderId;
        }

        public void ClearSelection()
        {
            _attackerId = null;
            _defenderId = null;
        }

        public bool RollOnce(out DiceRollResult? result, out string error)
        {
            if (_attackerId == null || _defenderId == null)
            {
                result = null;
                error = "Selecciona atacante y defensor en el mapa.";
                return false;
            }
            return RollOnce(_attackerId, _defenderId, out result, out error);
        }

        public bool RollOnce(string attackerId, string defenderId, out DiceRollResult? result, out string error)
        {
            result = null;
            error  = string.Empty;

            if (_engine.State.Phase != Phase.Attack)
            {
                error = "No estás en fase de ataque.";
                return false;
            }

            var state = _engine.State;

            if (!state.Territories.TryGetValue(attackerId, out var att))
            {
                error = "Atacante inexistente.";
                return false;
            }
            if (!state.Territories.TryGetValue(defenderId, out var def))
            {
                error = "Defensor inexistente.";
                return false;
            }

            if (att.OwnerId != state.CurrentPlayerId)
            {
                error = "El atacante no te pertenece.";
                return false;
            }
            if (def.OwnerId == state.CurrentPlayerId)
            {
                error = "No puedes atacar un territorio propio.";
                return false;
            }
            if (att.Troops <= 1)
            {
                error = "El atacante necesita más de 1 tropa para atacar.";
                return false;
            }

            int attackerDice = Math.Clamp(att.Troops - 1, 1, 3);
            int defenderDice = Math.Clamp(def.Troops,       1, 2);

            int[] aRolls = RollDice(attackerDice);
            int[] dRolls = RollDice(defenderDice);

            Array.Sort(aRolls); Array.Reverse(aRolls);
            Array.Sort(dRolls); Array.Reverse(dRolls);

            int comps = Math.Min(aRolls.Length, dRolls.Length);
            int aLosses = 0, dLosses = 0;
            for (int i = 0; i < comps; i++)
            {
                if (aRolls[i] > dRolls[i]) dLosses++;
                else                       aLosses++;
            }

            int aRemove = Math.Min(aLosses, att.Troops - 1);
            int dRemove = Math.Min(dLosses, def.Troops);

            att.Troops -= aRemove;
            def.Troops -= dRemove;

            bool captured = false;

            if (def.Troops <= 0)
            {
                captured = true;

                int move = Math.Max(1, Math.Min(attackerDice, att.Troops - 1));
                if (move > 0)
                {
                    att.Troops -= move;
                    def.Troops  = move;
                }
                else
                {
                    if (att.Troops > 1)
                    {
                        att.Troops -= 1;
                        def.Troops  = 1;
                    }
                    else
                    {
                        def.Troops = 1;
                    }
                }

                def.OwnerId = att.OwnerId;
                _log?.Invoke($"Capturado: {defenderId}. {move} tropas movidas.");
            }

            result = new DiceRollResult
            {
                AttackerDice      = aRolls,
                DefenderDice      = dRolls,
                AttackerLosses    = aRemove,
                DefenderLosses    = dRemove,
                TerritoryCaptured = captured
            };

            _attackerId = attackerId;
            _defenderId = defenderId;
            return true;
        }

        private int[] RollDice(int count)
        {
            var arr = new int[count];
            for (int i = 0; i < count; i++)
                arr[i] = _rng.Next(1, 7);
            return arr;
        }
    }
}