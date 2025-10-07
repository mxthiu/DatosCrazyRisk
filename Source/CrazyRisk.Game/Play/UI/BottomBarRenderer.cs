#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Renderiza y gestiona la barra inferior con botones:
    /// Confirmar, Deshacer, Cancelar, Siguiente (por defecto).
    /// - Dibuja los botones
    /// - Maneja hover (pasando el mouse)
    /// - Expone hit-testing para detectar clicks
    /// No toca el estado del juego; solo UI.
    /// </summary>
    public sealed class BottomBarRenderer
    {
        public enum BottomAction
        {
            None = 0,
            Confirmar,
            Deshacer,
            Cancelar,
            Siguiente
        }

        private readonly SpriteFont _font;
        private readonly Texture2D _pixel;

        private readonly List<ButtonItem> _buttons = new();
        private Rectangle _lastArea;

        private struct ButtonItem
        {
            public Rectangle Bounds;
            public string Text;
            public bool Hover;
            public BottomAction Action;
        }

        public BottomBarRenderer(SpriteFont font, Texture2D pixel)
        {
            _font  = font  ?? throw new ArgumentNullException(nameof(font));
            _pixel = pixel ?? throw new ArgumentNullException(nameof(pixel));
            SetDefaultButtons();
        }

        /// <summary>
        /// Restablece el set de botones por defecto (Confirmar, Deshacer, Cancelar, Siguiente).
        /// </summary>
        public void SetDefaultButtons()
        {
            _buttons.Clear();
            _buttons.Add(new ButtonItem { Text = "Confirmar", Action = BottomAction.Confirmar });
            _buttons.Add(new ButtonItem { Text = "Deshacer",  Action = BottomAction.Deshacer  });
            _buttons.Add(new ButtonItem { Text = "Cancelar",  Action = BottomAction.Cancelar  });
            _buttons.Add(new ButtonItem { Text = "Siguiente", Action = BottomAction.Siguiente });
            _lastArea = Rectangle.Empty; // forzar relayout
        }

        /// <summary>
        /// Reemplaza el conjunto de botones (texto + acción). (1..6 recomendado)
        /// </summary>
        public void SetButtons(params (string text, BottomAction action)[] buttons)
        {
            _buttons.Clear();
            foreach (var b in buttons)
            {
                _buttons.Add(new ButtonItem { Text = b.text, Action = b.action });
            }
            _lastArea = Rectangle.Empty; // forzar relayout
        }

        /// <summary>
        /// Dibuja la barra y los botones. Si drawBackground=true, pinta una franja de fondo.
        /// </summary>
        public void Draw(SpriteBatch sb, Rectangle area, bool drawBackground = false)
        {
            if (drawBackground)
            {
                sb.Draw(_pixel, area, new Color(15, 15, 18, 255));
                DrawRect(sb, area, new Color(255, 255, 255, 40), 1);
            }

            EnsureLayout(area);

            // Dibujo de botones (sin CollectionsMarshal)
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                var bgc = b.Hover ? new Color(255, 255, 255, 210) : new Color(255, 255, 255, 160);
                // sombra ligera
                sb.Draw(_pixel, new Rectangle(b.Bounds.X + 3, b.Bounds.Y + 3, b.Bounds.Width, b.Bounds.Height), new Color(0, 0, 0, 70));
                // cuerpo
                sb.Draw(_pixel, b.Bounds, bgc);
                DrawRect(sb, b.Bounds, new Color(0, 0, 0, 190), 2);

                var s = _font.MeasureString(b.Text);
                var tx = b.Bounds.X + (b.Bounds.Width - s.X) * 0.5f;
                var ty = b.Bounds.Y + (b.Bounds.Height - s.Y) * 0.5f;
                sb.DrawString(_font, b.Text, new Vector2(tx, ty), Color.Black);
            }
        }

        /// <summary>
        /// Actualiza el estado de hover en base a la posición del mouse.
        /// </summary>
        public void UpdateHover(Point mousePos)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var bi = _buttons[i];
                bi.Hover = bi.Bounds.Contains(mousePos);
                _buttons[i] = bi;
            }
        }

        /// <summary>
        /// Devuelve la acción del botón clicado, o None si no se clicó ningún botón.
        /// </summary>
        public BottomAction HitTestClick(Point mousePos)
        {
            foreach (var b in _buttons)
            {
                if (b.Bounds.Contains(mousePos))
                    return b.Action;
            }
            return BottomAction.None;
        }

        private void EnsureLayout(Rectangle area)
        {
            if (area == _lastArea && _buttons.Count > 0 && _buttons[0].Bounds.Width > 0)
                return;

            _lastArea = area;

            if (_buttons.Count == 0) return;

            // Layout: botones alineados a la izquierda con gap constante.
            int bw = 160;
            int bh = Math.Min(44, Math.Max(36, area.Height - 16));
            int gap = 12;
            int startX = area.X + 12;
            int y = area.Y + (area.Height - bh) / 2;

            for (int i = 0; i < _buttons.Count; i++)
            {
                var r = new Rectangle(startX + i * (bw + gap), y, bw, bh);
                var bi = _buttons[i];
                bi.Bounds = r;
                _buttons[i] = bi;
            }
        }

        private void DrawRect(SpriteBatch sb, Rectangle r, Color c, int thickness)
        {
            sb.Draw(_pixel, new Rectangle(r.Left, r.Top, r.Width, thickness), c);
            sb.Draw(_pixel, new Rectangle(r.Left, r.Bottom - thickness, r.Width, thickness), c);
            sb.Draw(_pixel, new Rectangle(r.Left, r.Top, thickness, r.Height), c);
            sb.Draw(_pixel, new Rectangle(r.Right - thickness, r.Top, thickness, r.Height), c);
        }
    }
}
