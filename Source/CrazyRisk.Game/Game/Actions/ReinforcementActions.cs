// Source/CrazyRisk.Game/Game/Actions/ReinforcementActions.cs
using System;
using CrazyRiskGame.Game.Abstractions;

namespace CrazyRiskGame.Game.Actions
{
    public sealed class ReinforcementActions
    {
        private readonly IReinforcementBackend backend;
        private readonly ILogSink log;

        public ReinforcementActions(IReinforcementBackend backend, ILogSink log)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool PlaceMany(string territoryId, int count)
        {
            if (string.IsNullOrWhiteSpace(territoryId))
            {
                log.Warn("Refuerzos: territorio no válido.");
                return false;
            }
            if (count <= 0)
            {
                log.Warn("Refuerzos: cantidad debe ser mayor a 0.");
                return false;
            }

            int toPlace = Math.Min(count, backend.ReinforcementsRemaining);
            if (toPlace <= 0)
            {
                log.Warn("Refuerzos: no hay refuerzos pendientes.");
                return false;
            }

            int placed = 0;
            for (int i = 0; i < toPlace; i++)
            {
                try
                {
                    backend.PlaceOne(territoryId);
                    placed++;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                {
                    log.Error($"Refuerzos: no se pudo colocar en '{territoryId}': {ex.Message}");
                    break;
                }
            }

            if (placed == toPlace)
            {
                log.Info($"Refuerzos: +{placed} en {territoryId}.");
                return true;
            }
            else if (placed > 0)
            {
                log.Warn($"Refuerzos: solo se colocaron {placed} de {toPlace} en {territoryId}.");
                return false;
            }
            else
            {
                log.Error("Refuerzos: acción inválida, no se colocó ninguno.");
                return false;
            }
        }
    }
}
