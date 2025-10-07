using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Controllers
{
    /// <summary>
    /// Lleva la selección/hover del territorio según la fase.
    /// Regla mínima: en Refuerzos, sólo puedes seleccionar territorios propios.
    /// </summary>
    public sealed class MapSelectionController
    {
        private readonly GameEngine _engine;

        public string? Hover { get; private set; }
        public string? Selected { get; private set; }

        public MapSelectionController(GameEngine engine)
        {
            _engine = engine;
        }

        public void SetHover(string? id) => Hover = id;

        public void ClickOn(string? id)
        {
            if (id is null) return;

            switch (_engine.State.Phase)
            {
                case Phase.Reinforcement:
                    if (_engine.State.Territories.TryGetValue(id, out var t) &&
                        t.OwnerId == _engine.State.CurrentPlayerId)
                        Selected = id;
                    break;

                case Phase.Attack:
                    // Por ahora sólo marca selección visual.
                    Selected = id;
                    break;

                case Phase.Fortify:
                    // Deja la selección libre; la lógica de mover se hace afuera.
                    Selected = id;
                    break;
            }
        }

        public void ClearSelection() => Selected = null;
    }
}
