#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Dibuja la barra superior (HUD) con:
    /// - Jugador actual (con “píldora” del color del owner)
    /// - Fase actual
    /// - Refuerzos restantes (solo en Reinforcement)
    /// - Texto de hint contextual
    /// No tiene estado; depende solo de parámetros.
    /// </summary>
    public sealed class HUDTopRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _pixel;

        public HUDTopRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _pixel = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        /// <summary>
        /// Render principal.
        /// </summary>
        /// <param name="sb">SpriteBatch activo.</param>
        /// <param name="area">Rectángulo de la barra superior.</param>
        /// <param name="engine">GameEngine con el estado actual.</param>
        /// <param name="getOwnerColor">Func para mapear ownerId -> Color (usa OwnerColorPalette.GetColor).</param>
        /// <param name="extraHint">Texto opcional al centro (por ejemplo, instrucciones de la fase).</param>
        public void Draw(SpriteBatch sb, Rectangle area, GameEngine? engine, Func<int, Color> getOwnerColor, string? extraHint = null)
        {
            // Fondo y borde
            sb.Draw(_pixel, area, new Color(15, 15, 18, 255));
            DrawRect(sb, area, new Color(255, 255, 255, 40), 1);

            if (engine == null) return;

            // Lado izquierdo: “píldora” con color del jugador + nombre/fase
            int curId = engine.State.CurrentPlayerId;
            var pill = new Rectangle(area.X + 12, area.Y + 12, 22, area.Height - 24);
            var c = getOwnerColor(curId);
            sb.Draw(_pixel, pill, c);
            DrawRect(sb, pill, new Color(0, 0, 0, 160), 1);

            // Nombre jugador
            var currentPlayer = engine.State.Players.Find(p => p.Id == curId)?.Name ?? $"J{curId}";
            string leftText1 = $"Jugador: {currentPlayer}";
            string leftText2 = $"Fase: {engine.State.Phase}";

            var t1Pos = new Vector2(pill.Right + 8, area.Y + 10);
            var t2Pos = new Vector2(pill.Right + 8, area.Y + 34);
            sb.DrawString(_font, leftText1, t1Pos, Color.White);
            sb.DrawString(_font, leftText2, t2Pos, Color.White);

            // Centro: hint contextual (opcional)
            if (!string.IsNullOrWhiteSpace(extraHint))
            {
                var size = _font.MeasureString(extraHint);
                var pos = new Vector2(
                    area.X + (area.Width - size.X) * 0.5f,
                    area.Y + (area.Height - size.Y) * 0.5f
                );
                sb.DrawString(_font, extraHint, pos, Color.White);
            }

            // Derecha: refuerzos visibles solo en Reinforcement
            if (engine.State.Phase == Phase.Reinforcement)
            {
                string rf = $"Refuerzos: {engine.State.ReinforcementsRemaining}";
                var sz = _font.MeasureString(rf);
                var pos = new Vector2(area.Right - 12 - sz.X, area.Y + 12);
                sb.DrawString(_font, rf, pos, Color.White);
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
