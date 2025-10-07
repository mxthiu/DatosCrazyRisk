namespace CrazyRiskGame.Game.Abstractions
{
    /// <summary>
    /// Backend mínimo para acciones de refuerzos sin Try*.
    /// La implementación concreta decide cómo aplicar el refuerzo.
    /// </summary>
    public interface IReinforcementBackend
    {
        /// <summary>Refuerzos pendientes del jugador actual.</summary>
        int ReinforcementsRemaining { get; }

        /// <summary>Coloca exactamente 1 refuerzo en el territorio dado (sin Try*).</summary>
        void PlaceOne(string territoryId);
    }
}
