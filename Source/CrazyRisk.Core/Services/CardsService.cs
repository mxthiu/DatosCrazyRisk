#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrazyRisk.Core
{
    /// <summary>
    /// Tipos de carta base (estilo Risk). Wild actúa como comodín.
    /// </summary>
    public enum CardKind
    {
        Infantry = 0,
        Cavalry  = 1,
        Artillery= 2,
        Wild     = 3
    }

    /// <summary>
    /// Carta inmutable con Id único.
    /// </summary>
    public sealed class Card
    {
        public int Id { get; }
        public CardKind Kind { get; }

        public Card(int id, CardKind kind)
        {
            Id = id;
            Kind = kind;
        }

        public override string ToString() => $"#{Id}:{Kind}";
    }

    /// <summary>
    /// Servicio de cartas y canjes (serie "Fibonacci" 4,6,10,16,26,...).
    /// - Mantiene manos por jugador.
    /// - Valida tríos (tres iguales; tres distintos; con Wild).
    /// - Calcula bono incremental usando serie 4,6,... (suma de los dos anteriores).
    /// </summary>
    public sealed class CardsService
    {
        private readonly Dictionary<int, List<Card>> _cardsByPlayer = new();
        private int _nextCardId = 1;
        private int _tradeCount = 0; // cuántos canjes se han hecho (global)

        // Semillas de la serie (4, 6, 10, 16, 26, 42, ...)
        private int _fibA = 4;
        private int _fibB = 6;

        /// <summary>
        /// Devuelve lectura de la mano del jugador (lista interna clonada).
        /// </summary>
        public IReadOnlyList<Card> GetPlayerCards(int playerId)
        {
            if (!_cardsByPlayer.TryGetValue(playerId, out var list))
                return Array.Empty<Card>();
            return list.ToArray();
        }

        /// <summary>
        /// Añade una carta concreta a un jugador (útil para test / recompensas específicas).
        /// </summary>
        public Card AddCardToPlayer(int playerId, CardKind kind)
        {
            if (!_cardsByPlayer.TryGetValue(playerId, out var list))
            {
                list = new List<Card>(8);
                _cardsByPlayer[playerId] = list;
            }

            var card = new Card(_nextCardId++, kind);
            list.Add(card);
            return card;
        }

        /// <summary>
        /// Otorga una carta aleatoria (entre Infantry/Cavalry/Artillery, con baja prob. de Wild si se desea).
        /// </summary>
        public Card AwardRandomCard(int playerId, Random rng, float wildChance = 0.0f)
        {
            CardKind kind;
            if (wildChance > 0 && rng.NextDouble() < wildChance)
                kind = CardKind.Wild;
            else
                kind = (CardKind)rng.Next(0, 3); // 0..2 (Infantry/Cavalry/Artillery)

            return AddCardToPlayer(playerId, kind);
        }

        /// <summary>
        /// Próximo bono de canje, sin consumir cartas ni avanzar la serie.
        /// Serie: 4, 6, 10, 16, 26, 42, ...
        /// </summary>
        public int PreviewNextTradeBonus()
        {
            // El primer canje da _fibA, el segundo _fibB, luego sumas sucesivas.
            if (_tradeCount == 0) return _fibA;
            if (_tradeCount == 1) return _fibB;

            // Calcula el término actual sin avanzar el estado
            int a = _fibA, b = _fibB;
            for (int i = 2; i <= _tradeCount; i++)
            {
                int next = a + b;
                a = b; b = next;
            }
            return b;
        }

        /// <summary>
        /// Verifica si una terna de cartas (ids) forma un set válido.
        /// Reglas:
        /// - 3 iguales (no contando Wild como "igual", aunque puede sustituir).
        /// - 3 distintos (Infantry, Cavalry, Artillery)
        /// - Cualquier combinación con >=1 Wild que pueda completar una de las dos reglas.
        /// </summary>
        public bool CanTradeTriplet(int playerId, IReadOnlyList<int> cardIds, out string error)
        {
            error = "";
            if (cardIds == null || cardIds.Count != 3) { error = "Debes elegir exactamente 3 cartas."; return false; }

            // Trae cartas del jugador y resuelve selección
            var hand = GetPlayerCards(playerId);
            var pick = hand.Where(c => cardIds.Contains(c.Id)).ToArray();
            if (pick.Length != 3) { error = "Las cartas seleccionadas no pertenecen al jugador."; return false; }

            // Conteos por tipo
            int wilds = pick.Count(c => c.Kind == CardKind.Wild);
            var basics = pick.Where(c => c.Kind != CardKind.Wild).Select(c => c.Kind).ToList();

            // caso 1: 3 básicos iguales (permitiendo wilds que sustituyen)
            if (IsThreeOfAKind(basics, wilds)) return true;

            // caso 2: 3 básicos todos distintos (permitiendo wilds que sustituyen)
            if (IsThreeAllDifferent(basics, wilds)) return true;

            error = "La combinación no es un trío válido.";
            return false;
        }

        /// <summary>
        /// Consume las cartas (si son válidas) y devuelve el bono de tropas otorgado.
        /// </summary>
        public bool TradeTriplet(int playerId, IReadOnlyList<int> cardIds, out int troopsAwarded, out string error)
        {
            troopsAwarded = 0;
            if (!CanTradeTriplet(playerId, cardIds, out error)) return false;

            // Remover cartas de la mano
            var hand = _cardsByPlayer[playerId];
            var toRemove = hand.Where(c => cardIds.Contains(c.Id)).ToList();
            foreach (var c in toRemove)
                hand.Remove(c);

            // Calcular bono actual y avanzar serie
            troopsAwarded = PreviewNextTradeBonus();
            AdvanceTradeSeries();

            return true;
        }

        /// <summary>
        /// Resetea la serie de canjes (por ejemplo, al iniciar nueva partida).
        /// </summary>
        public void ResetTrades()
        {
            _tradeCount = 0;
            _fibA = 4;
            _fibB = 6;
        }

        /// <summary>
        /// Limpia todas las manos (útil para reiniciar).
        /// </summary>
        public void ClearAllHands()
        {
            _cardsByPlayer.Clear();
            _nextCardId = 1;
        }

        // ===================== Helpers de validación =====================

        private static bool IsThreeOfAKind(IReadOnlyList<CardKind> basics, int wilds)
        {
            // Queremos terminar con 3 del mismo tipo básico (Inf/Cav/Art).
            // Toma el tipo mayoritario (si hay) y verifica que con wilds se llega a 3.
            // Si basics está vacío pero hay wilds, no se puede decidir un tipo → false.
            if (basics.Count == 0) return false;

            foreach (CardKind k in new[] { CardKind.Infantry, CardKind.Cavalry, CardKind.Artillery })
            {
                int count = basics.Count(b => b == k);
                if (count + wilds >= 3) return true;
            }
            return false;
        }

        private static bool IsThreeAllDifferent(IReadOnlyList<CardKind> basics, int wilds)
        {
            // Meta: cubrir los 3 básicos distintos (Inf, Cav, Art) con ayuda de wilds.
            var set = new HashSet<CardKind>(basics.Where(k => k != CardKind.Wild));
            int distinctBasics = set.Count;

            // Si hay más de 3 básicos distintos (imposible con 3 cartas) o repetidos, evaluamos déficit.
            // Los 3 objetivos son Inf, Cav, Art: nos faltan (3 - distinctBasics) tipos.
            if (distinctBasics > 3) return false;

            int deficit = 3 - distinctBasics;

            // Necesitamos cubrir ese déficit con wilds, y además que los básicos elegidos
            // no incluyan repetidos extra (no importa si hay repetidos; solo cuenta cubrir 3 tipos).
            return wilds >= deficit;
        }

        private void AdvanceTradeSeries()
        {
            // Avanza contador y términos de la serie 4,6,10,16,26,...
            if (_tradeCount == 0)
            {
                _tradeCount = 1;
                return;
            }
            if (_tradeCount == 1)
            {
                _tradeCount = 2;
                return;
            }

            int next = _fibA + _fibB;
            _fibA = _fibB;
            _fibB = next;
            _tradeCount++;
        }
    }
}
