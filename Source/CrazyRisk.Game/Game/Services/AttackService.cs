#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    public sealed class AttackService
    {
        private readonly GameEngine _engine;

        public AttackService(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public bool TrySetAttacker(string territoryId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(territoryId)) { error = "Territorio inválido."; return false; }
            return _engine.SetAttackFrom(territoryId, out error);
        }

        public bool TrySetDefender(string territoryId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(territoryId)) { error = "Territorio inválido."; return false; }
            return _engine.SetAttackTo(territoryId, out error);
        }

        public DiceRollResult? RollOnce(out string error)
        {
            return _engine.RollAttackOnce(out error);
        }

        public void ClearSelection()
        {
            _engine.State.PendingAttackFrom = null;
            _engine.State.PendingAttackTo = null;
        }
    }
}
