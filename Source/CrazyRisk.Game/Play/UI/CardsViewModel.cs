#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Item simple para pintar en UI (id, tipo y si está seleccionado).
    /// </summary>
    public readonly struct CardVM
    {
        public int Id { get; }
        public CardKind Kind { get; }
        public bool Selected { get; }

        public CardVM(int id, CardKind kind, bool selected)
        {
            Id = id;
            Kind = kind;
            Selected = selected;
        }

        public override string ToString() => $"[{Id}] {Kind}" + (Selected ? " *" : "");
    }

    /// <summary>
    /// ViewModel para la pestaña "Cartas".
    /// - Expone la mano del jugador.
    /// - Permite seleccionar/deseleccionar 3 cartas.
    /// - Valida y canjea tríos usando CardsService.
    /// - Entrega vista previa del próximo bono.
    /// - Otorga cartas al capturar un territorio.
    /// </summary>
    public sealed class CardsViewModel
    {
        private readonly CardsService _cardsService;
        private readonly int _playerId;
        private readonly Random _rng;

        // selección actual (ids de carta)
        private readonly HashSet<int> _selected = new();

        /// <summary>
        /// Evento que la UI puede enganchar para refrescar (lista cambió / selección cambió / canje cambió).
        /// </summary>
        public event Action? Changed;

        public CardsViewModel(CardsService cardsService, int playerId, Random rng)
        {
            _cardsService = cardsService ?? throw new ArgumentNullException(nameof(cardsService));
            _playerId = playerId;
            _rng = rng ?? new Random();
        }

        /// <summary>
        /// Mano actual del jugador (lectura directa del servicio).
        /// </summary>
        public IReadOnlyList<Card> Hand => _cardsService.GetPlayerCards(_playerId);

        /// <summary>
        /// Representación lista para dibujar (incluye "Selected" por carta).
        /// </summary>
        public IReadOnlyList<CardVM> BuildCardList()
        {
            var hand = Hand;
            var list = new List<CardVM>(hand.Count);
            foreach (var c in hand)
                list.Add(new CardVM(c.Id, c.Kind, _selected.Contains(c.Id)));
            return list;
        }

        /// <summary>
        /// Alterna selección de una carta por id.
        /// Máximo 3 seleccionadas; si hay 3 y tocas otra, ignoramos (o podrías auto-deseleccionar la más antigua).
        /// </summary>
        public void ToggleSelect(int cardId)
        {
            if (_selected.Contains(cardId))
                _selected.Remove(cardId);
            else
            {
                if (_selected.Count >= 3) return;
                _selected.Add(cardId);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Limpia la selección actual.
        /// </summary>
        public void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// Devuelve ids actualmente seleccionados (copia).
        /// </summary>
        public IReadOnlyList<int> GetSelectionIds() => _selected.ToArray();

        /// <summary>
        /// ¿La selección actual forma un trío válido?
        /// </summary>
        public bool CanTrade(out string error)
        {
            error = "";
            var sel = GetSelectionIds();
            if (sel.Count != 3) { error = "Debes seleccionar 3 cartas."; return false; }
            return _cardsService.CanTradeTriplet(_playerId, sel, out error);
        }

        /// <summary>
        /// Intenta canjear la selección actual. Si es válido:
        /// - Remueve cartas de la mano
        /// - Calcula el bono
        /// - Limpia selección
        /// - Lanza Changed
        /// </summary>
        public bool TradeSelected(out int troopsAwarded, out string error)
        {
            troopsAwarded = 0;
            var sel = GetSelectionIds();
            if (sel.Count != 3)
            {
                error = "Debes seleccionar 3 cartas.";
                return false;
            }

            var ok = _cardsService.TradeTriplet(_playerId, sel, out troopsAwarded, out error);
            if (ok)
            {
                _selected.Clear();
                Changed?.Invoke();
            }
            return ok;
        }

        /// <summary>
        /// Vista previa del próximo bono de canje (sin consumir nada).
        /// </summary>
        public int PreviewNextBonus() => _cardsService.PreviewNextTradeBonus();

        /// <summary>
        /// Otorga una carta al jugador (p.ej., al capturar un territorio).
        /// </summary>
        public Card AwardAfterCapture(float wildChance = 0.0f)
        {
            var c = _cardsService.AwardRandomCard(_playerId, _rng, wildChance);
            Changed?.Invoke();
            return c;
        }

        /// <summary>
        /// Añade una carta específica (útil para pruebas/UI).
        /// </summary>
        public Card AddSpecific(CardKind kind)
        {
            var c = _cardsService.AddCardToPlayer(_playerId, kind);
            Changed?.Invoke();
            return c;
        }
    }
}
