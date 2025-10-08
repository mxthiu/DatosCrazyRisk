#nullable enable
using System;
using System.Collections.Generic;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>
    /// Placeholder mínimo para que el juego compile y puedas iterar la UI de Cartas.
    /// Guarda un mazo muy simple en memoria.
    /// </summary>
    public sealed class CardsService
    {
        private readonly Stack<string> _deck = new();
        private readonly List<string> _hand = new();

        public IReadOnlyList<string> Hand => _hand;

        public CardsService()
        {
            // Mazo mínimo de prueba
            var seed = new[] { "Infantería", "Caballería", "Artillería", "Comodín" };
            // duplicamos un poco para que haya más
            for (int i = 0; i < 3; i++)
                foreach (var c in seed)
                    _deck.Push(c);
        }

        public bool CanDraw => _deck.Count > 0;

        public bool TryDraw(out string? card)
        {
            if (_deck.Count == 0) { card = null; return false; }
            card = _deck.Pop();
            _hand.Add(card);
            return true;
        }

        public bool TryTurnInSet(out int bonus)
        {
            // Placeholder: si tienes >=3 cartas, canjéas y recibes +4
            if (_hand.Count >= 3)
            {
                _hand.RemoveRange(0, 3);
                bonus = 4;
                return true;
            }
            bonus = 0;
            return false;
        }
    }
}