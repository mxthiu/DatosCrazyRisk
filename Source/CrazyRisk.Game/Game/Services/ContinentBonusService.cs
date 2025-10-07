#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CrazyRisk.Core;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Calcula y aplica bonos por continente cuando un jugador posee todos
    /// los territorios definidos para ese continente.
    /// No modifica GameEngine salvo cuando se invoca ApplyBonusForCurrentPlayer(...).
    /// </summary>
    public sealed class ContinentBonusService
    {
        public sealed class ContinentDef
        {
            public string Name { get; }
            public int Bonus { get; }
            public IReadOnlyList<string> Territories { get; }

            public ContinentDef(string name, int bonus, IEnumerable<string> territories)
            {
                Name = name;
                Bonus = bonus;
                Territories = territories.ToArray();
            }
        }

        private readonly List<ContinentDef> _continents;

        // Eventos (para log de UI)
        public event Action<string>? OnInfo;

        public ContinentBonusService(IEnumerable<ContinentDef> continents)
        {
            _continents = continents?.ToList() ?? throw new ArgumentNullException(nameof(continents));
        }

        /// <summary>
        /// Devuelve la suma de bonos por los continentes que SON controlados totalmente por playerId.
        /// </summary>
        public int GetBonusForPlayer(GameEngine engine, int playerId, out List<string> continentsOwned)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            continentsOwned = new List<string>();
            int total = 0;

            foreach (var c in _continents)
            {
                bool ownsAll = true;
                foreach (var tid in c.Territories)
                {
                    if (!engine.State.Territories.TryGetValue(tid, out var st) || st.OwnerId != playerId)
                    {
                        ownsAll = false;
                        break;
                    }
                }

                if (ownsAll)
                {
                    total += c.Bonus;
                    continentsOwned.Add(c.Name);
                }
            }
            return total;
        }

        /// <summary>
        /// Calcula y aplica el bono de continentes al jugador actual.
        /// Devuelve el bono aplicado (0 si no aplica).
        /// </summary>
        public int ApplyBonusForCurrentPlayer(GameEngine engine)
        {
            var pid = engine.State.CurrentPlayerId;
            var bonus = GetBonusForPlayer(engine, pid, out var owned);
            if (bonus > 0)
            {
                engine.State.ReinforcementsRemaining += bonus;
                if (owned.Count > 0)
                {
                    OnInfo?.Invoke($"Bono de continentes (+{bonus}): {string.Join(", ", owned)}");
                }
                else
                {
                    OnInfo?.Invoke($"Bono de continentes aplicado: +{bonus}");
                }
            }
            return bonus;
        }

        /// <summary>
        /// Crea una definición por defecto basada en los prefijos de los ids (NORTEAMERICA_, SURAMERICA_, etc.).
        /// Ajusta los ids y bonos según tu mapa real.
        /// </summary>
        public static ContinentBonusService CreateDefault()
        {
            // Bonos típicos orientativos para Risk clásico (ajústalos a tu diseño):
            // NA: +5, SA: +2, EU: +5, AF: +3, AS: +7, OC: +2
            // Los territorios se detectan por prefijo del id que usamos en el proyecto.
            var NA = new ContinentDef(
                "Norteamérica", 5, new[]
                {
                    "NORTEAMERICA_CANADA","NORTEAMERICA_ESTADOS_UNIDOS","NORTEAMERICA_MEXICO",
                    "NORTEAMERICA_GROENLANDIA","NORTEAMERICA_CUBA","NORTEAMERICA_HAITI","NORTEAMERICA_GUATEMALA"
                });

            var SA = new ContinentDef(
                "Suramérica", 2, new[]
                {
                    "SURAMERICA_BRASIL","SURAMERICA_ARGENTINA","SURAMERICA_CHILE",
                    "SURAMERICA_PERU","SURAMERICA_COLOMBIA","SURAMERICA_URUGUAY"
                });

            var EU = new ContinentDef(
                "Europa", 5, new[]
                {
                    "EUROPA_ESPANA","EUROPA_FRANCIA","EUROPA_ALEMANIA","EUROPA_ITALIA",
                    "EUROPA_NORUEGA","EUROPA_GRECIA"
                });

            var AF = new ContinentDef(
                "África", 3, new[]
                {
                    "AFRICA_EGIPTO","AFRICA_NIGERIA","AFRICA_SUDAFRICA","AFRICA_MARRUECOS",
                    "AFRICA_KENIA","AFRICA_ETIOPIA","AFRICA_MADAGASCAR"
                });

            var AS = new ContinentDef(
                "Asia", 7, new[]
                {
                    "ASIA_CHINA","ASIA_INDIA","ASIA_JAPON","ASIA_COREA_DEL_SUR",
                    "ASIA_ARABIA_SAUDITA","ASIA_IRAN","ASIA_ISRAEL","ASIA_TURQUIA","ASIA_RUSIA",
                    "ASIA_INDONESIA","ASIA_TAILANDIA","ASIA_FILIPINAS"
                });

            var OC = new ContinentDef(
                "Oceanía", 2, new[]
                {
                    "OCEANIA_AUSTRALIA","OCEANIA_NUEVA_ZELANDA","OCEANIA_PAPUA_NUEVA_GUINEA","OCEANIA_FIYI"
                });

            return new ContinentBonusService(new[] { NA, SA, EU, AF, AS, OC });
        }
    }
}
