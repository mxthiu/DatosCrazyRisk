#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
// Alias al enum del Core
using CardKind = CrazyRisk.Core.CardKind;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Layout y pintado del panel de cartas (pestaña "Cartas").
    /// - Dibuja una grilla de cartas (id + tipo + seleccionado).
    /// - Muestra el "Próximo bono" (preview).
    /// - Dibuja botón "Canjear set".
    /// - Expone hit-testing para detectar clicks en cartas / botón.
    /// </summary>
    public sealed class CardsPanelRenderer
    {
        // Config de layout
        public int Columns { get; set; } = 3;
        public int CardMinWidth { get; set; } = 92;
        public int CardMinHeight { get; set; } = 116;
        public int Gap { get; set; } = 10;
        public int HeaderHeight { get; set; } = 24;
        public int FooterHeight { get; set; } = 60;
        public int BadgeHeight { get; set; } = 22;

        // Áreas calculadas
        public Rectangle PanelArea { get; private set; }
        public Rectangle GridArea { get; private set; }
        public Rectangle TradeButtonRect { get; private set; }
        public Rectangle PreviewBadgeRect { get; private set; }

        // Mapa cartaId -> rect
        private readonly Dictionary<int, Rectangle> _cardRects = new();

        // Colores base
        private static readonly Color CardBg = new Color(255, 255, 255, 28);
        private static readonly Color CardBorder = new Color(255, 255, 255, 90);
        private static readonly Color CardSelected = new Color(120, 200, 255, 80);
        private static readonly Color TextFg = Color.White;
        private static readonly Color Shadow = new Color(0, 0, 0, 70);
        private static readonly Color ButtonBg = new Color(255, 255, 255, 210);
        private static readonly Color ButtonBorder = new Color(0, 0, 0, 190);
        private static readonly Color BadgeBg = new Color(0, 0, 0, 150);
        private static readonly Color BadgeBorder = new Color(255, 255, 255, 90);

        public void BuildLayout(Rectangle panelArea, IReadOnlyList<CardVM> items)
        {
            PanelArea = panelArea;

            // Header y footer reducen área de grilla
            var usable = new Rectangle(
                panelArea.X,
                panelArea.Y + HeaderHeight,
                panelArea.Width,
                panelArea.Height - HeaderHeight - FooterHeight
            );

            GridArea = new Rectangle(usable.X + Gap, usable.Y + Gap, usable.Width - Gap * 2, usable.Height - Gap * 2);

            // Botón "Canjear set"
            TradeButtonRect = new Rectangle(
                panelArea.X + Gap,
                panelArea.Bottom - FooterHeight + (FooterHeight - 40) / 2,
                panelArea.Width - Gap * 2,
                40
            );

            // Badge de preview (arriba derecha del header)
            PreviewBadgeRect = new Rectangle(
                panelArea.Right - 140 - Gap,
                panelArea.Y + (HeaderHeight - BadgeHeight) / 2,
                140,
                BadgeHeight
            );

            _cardRects.Clear();
            if (items.Count == 0) return;

            // Calcular grilla
            int cols = Math.Max(1, Columns);
            int rows = (int)Math.Ceiling(items.Count / (float)cols);

            // ancho/alto de cada carta
            int cw = (GridArea.Width - Gap * (cols - 1)) / cols;
            int ch = (GridArea.Height - Gap * (rows - 1)) / Math.Max(1, rows);

            cw = Math.Max(CardMinWidth, cw);
            ch = Math.Max(CardMinHeight, ch);

            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (idx >= items.Count) break;
                    int x = GridArea.X + c * (cw + Gap);
                    int y = GridArea.Y + r * (ch + Gap);
                    _cardRects[items[idx].Id] = new Rectangle(x, y, cw, ch);
                    idx++;
                }
            }
        }

        public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont? font, IReadOnlyList<CardVM> items, int nextBonus)
        {
            if (font != null)
            {
                sb.DrawString(font, "Cartas", new Vector2(PanelArea.X + 6, PanelArea.Y + 4), TextFg);

                // Badge preview
                DrawRect(sb, pixel, PreviewBadgeRect, BadgeBorder, 1);
                sb.Draw(pixel, PreviewBadgeRect, BadgeBg);
                string preview = $"Próximo bono: {nextBonus}";
                var ms = font.MeasureString(preview);
                sb.DrawString(font, preview,
                    new Vector2(
                        PreviewBadgeRect.X + (PreviewBadgeRect.Width - ms.X) * 0.5f,
                        PreviewBadgeRect.Y + (PreviewBadgeRect.Height - ms.Y) * 0.5f
                    ), Color.White);
            }

            // Grilla
            foreach (var item in items)
            {
                if (!_cardRects.TryGetValue(item.Id, out var r)) continue;

                // fondo + selección
                sb.Draw(pixel, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), Shadow);
                sb.Draw(pixel, r, CardBg);
                if (item.Selected) sb.Draw(pixel, r, CardSelected);
                DrawRect(sb, pixel, r, CardBorder, 2);

                // header de carta
                var header = new Rectangle(r.X, r.Y, r.Width, 26);
                sb.Draw(pixel, header, new Color(255, 255, 255, 36));
                DrawRect(sb, pixel, header, new Color(255, 255, 255, 80), 1);

                if (font != null)
                {
                    string title = $"{item.Kind}";
                    var m1 = font.MeasureString(title);
                    sb.DrawString(font, title,
                        new Vector2(header.X + (header.Width - m1.X) * 0.5f, header.Y + (header.Height - m1.Y) * 0.5f),
                        Color.White);

                    string idText = $"#{item.Id}";
                    var m2 = font.MeasureString(idText);
                    sb.DrawString(font, idText,
                        new Vector2(r.X + (r.Width - m2.X) * 0.5f, r.Bottom - m2.Y - 6),
                        new Color(230, 230, 230));
                }

                // “Icono” central (geom simple)
                int inset = 10;
                var icon = new Rectangle(r.X + inset, r.Y + 32, r.Width - inset * 2, r.Height - 32 - 28);
                DrawIcon(sb, pixel, item.Kind, icon);
            }

            // Botón canjear
            DrawPrimaryButton(sb, pixel, font, TradeButtonRect, "Canjear set");
        }

        public int? HitTestCard(Point p)
        {
            foreach (var kv in _cardRects)
                if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        public bool HitTestTrade(Point p) => TradeButtonRect.Contains(p);

        // ===== helpers =====
        private static void DrawRect(SpriteBatch sb, Texture2D px, Rectangle r, Color c, int t)
        {
            sb.Draw(px, new Rectangle(r.Left, r.Top, r.Width, t), c);
            sb.Draw(px, new Rectangle(r.Left, r.Bottom - t, r.Width, t), c);
            sb.Draw(px, new Rectangle(r.Left, r.Top, t, r.Height), c);
            sb.Draw(px, new Rectangle(r.Right - t, r.Top, t, r.Height), c);
        }

        private static void DrawPrimaryButton(SpriteBatch sb, Texture2D px, SpriteFont? font, Rectangle r, string text)
        {
            sb.Draw(px, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0, 0, 0, 70));
            sb.Draw(px, r, ButtonBg);
            DrawRect(sb, px, r, ButtonBorder, 2);

            if (font != null)
            {
                var s = font.MeasureString(text);
                sb.DrawString(font, text,
                    new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f),
                    Color.Black);
            }
        }

        private static void DrawIcon(SpriteBatch sb, Texture2D px, CardKind kind, Rectangle area)
        {
            // Iconos geométricos según tipo (placeholder visual limpio)
            var mid = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            switch (kind)
            {
                case CardKind.Infantry:
                    // rectángulo vertical
                    var rod = new Rectangle(mid.X - area.Width / 16, area.Y + area.Height / 6, Math.Max(6, area.Width / 8), area.Height * 2 / 3);
                    sb.Draw(px, rod, new Color(120, 210, 255, 160));
                    DrawRect(sb, px, rod, new Color(255, 255, 255, 100), 1);
                    break;

                case CardKind.Cavalry:
                    // rombo
                    var rh = new[]
                    {
                        new Vector2(mid.X, area.Y),
                        new Vector2(area.Right, mid.Y),
                        new Vector2(mid.X, area.Bottom),
                        new Vector2(area.X, mid.Y),
                    };
                    FillConvex(sb, px, rh, new Color(160, 255, 160, 140));
                    break;

                case CardKind.Artillery:
                    // cañón simple (dos rects)
                    var baseRect = new Rectangle(area.X + area.Width / 6, mid.Y - 6, area.Width * 2 / 3, 12);
                    var tube = new Rectangle(mid.X - 4, area.Y + area.Height / 4, 8, area.Height / 2);
                    sb.Draw(px, baseRect, new Color(255, 180, 120, 140));
                    sb.Draw(px, tube, new Color(255, 180, 120, 180));
                    DrawRect(sb, px, baseRect, new Color(255, 255, 255, 90), 1);
                    DrawRect(sb, px, tube, new Color(255, 255, 255, 90), 1);
                    break;

                case CardKind.Wild:
                    // estrella
                    DrawStar(sb, px, mid, Math.Min(area.Width, area.Height) / 3, new Color(255, 230, 120, 180));
                    break;
            }
        }

        private static void FillConvex(SpriteBatch sb, Texture2D px, Vector2[] vertices, Color c)
        {
            // Relleno muy simple “scanline” aproximado (para 4 pts, rombo)
            if (vertices.Length != 4) return;
            var minY = (int)Math.Min(Math.Min(vertices[0].Y, vertices[1].Y), Math.Min(vertices[2].Y, vertices[3].Y));
            var maxY = (int)Math.Max(Math.Max(vertices[0].Y, vertices[1].Y), Math.Max(vertices[2].Y, vertices[3].Y));
            for (int y = minY; y <= maxY; y++)
            {
                // Intersecciones con los 4 segmentos
                var xs = new List<float>(4);
                for (int i = 0; i < 4; i++)
                {
                    var a = vertices[i];
                    var b = vertices[(i + 1) % 4];
                    if ((y >= a.Y && y < b.Y) || (y >= b.Y && y < a.Y))
                    {
                        float t = (y - a.Y) / (b.Y - a.Y);
                        xs.Add(a.X + (b.X - a.X) * t);
                    }
                }
                if (xs.Count >= 2)
                {
                    xs.Sort();
                    int x0 = (int)Math.Floor(xs[0]);
                    int x1 = (int)Math.Ceiling(xs[^1]);
                    sb.Draw(px, new Rectangle(x0, y, Math.Max(1, x1 - x0), 1), c);
                }
            }
        }

        private static void DrawStar(SpriteBatch sb, Texture2D px, Point center, int r, Color c)
        {
            // estrella de 8 puntas aproximada
            int d = Math.Max(2, r / 3);
            var rects = new[]
            {
                new Rectangle(center.X - d, center.Y - r, d*2, r*2), // vertical
                new Rectangle(center.X - r, center.Y - d, r*2, d*2), // horizontal
                new Rectangle(center.X - (int)(d*1.1f), center.Y - (int)(d*1.1f), (int)(d*2.2f), (int)(d*2.2f)),
            };
            foreach (var rr in rects) sb.Draw(px, rr, c);
        }
    }
}
