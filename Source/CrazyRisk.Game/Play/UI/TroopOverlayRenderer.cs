#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Dibuja una “burbuja” por territorio con el número de tropas y color del dueño.
    /// Calcula el centroide de cada máscara (gris) la primera vez y lo cachea.
    /// </summary>
    public sealed class TroopOverlayRenderer
    {
        private Rectangle _mapViewport;
        private float _mapScale = 1f;

        // id -> punto relativo a la textura de máscara (en pixeles del mapa sin escalar)
        private readonly Dictionary<string, Point> _centroidCache = new(StringComparer.Ordinal);

        // Aux UI
        private static readonly Color BubbleShadow = new Color(0, 0, 0, 80);
        private static readonly Color BubbleBorder = new Color(0, 0, 0, 200);
        private static readonly Color TextColor = Color.Black;
        private static readonly Color FallbackOwnerColor = new Color(200, 200, 200);

        /// <summary>
        /// Configura el viewport del mapa y la escala con la que se está dibujando.
        /// </summary>
        public void Configure(Rectangle mapViewport, float mapScale)
        {
            _mapViewport = mapViewport;
            _mapScale = mapScale <= 0 ? 1f : mapScale;
        }

        /// <summary>
        /// Dibuja las burbujas de tropas dadas las máscaras y el estado actual.
        /// </summary>
        /// <param name="sb">SpriteBatch activo (Begin ya llamado).</param>
        /// <param name="pixel">Textura blanca 1x1.</param>
        /// <param name="font">Fuente para texto.</param>
        /// <param name="territoryIds">IDs en el mismo orden que maneja tu juego.</param>
        /// <param name="maskGetter">Func para obtener la máscara de un territorio (o null).</param>
        /// <param name="troopsGetter">Func (id -> tropas).</param>
        /// <param name="ownerGetter">Func (id -> ownerId).</param>
        /// <param name="ownerColor">Func (ownerId -> Color).</param>
        public void Draw(
            SpriteBatch sb,
            Texture2D pixel,
            SpriteFont? font,
            IReadOnlyList<string> territoryIds,
            Func<string, Texture2D?> maskGetter,
            Func<string, int> troopsGetter,
            Func<string, int> ownerGetter,
            Func<int, Color> ownerColor)
        {
            if (territoryIds.Count == 0) return;

            foreach (var id in territoryIds)
            {
                // 1) Obtener/Calcular centroide (en coords de textura, sin escala)
                if (!_centroidCache.TryGetValue(id, out var center))
                {
                    var mask = maskGetter(id);
                    if (mask != null)
                    {
                        center = ComputeMaskCentroid(mask);
                        _centroidCache[id] = center;
                    }
                    else
                    {
                        // Si no hay máscara, coloca algo razonable (esquina superior-izq)
                        center = new Point(20, 20);
                        _centroidCache[id] = center;
                    }
                }

                // 2) Transformar a pantalla
                var screen = new Vector2(
                    _mapViewport.X + center.X * _mapScale,
                    _mapViewport.Y + center.Y * _mapScale
                );

                // 3) Datos
                int troops = Math.Max(0, troopsGetter(id));
                int ownerId = ownerGetter(id);
                var bg = ownerColor(ownerId);
                if (bg == default) bg = FallbackOwnerColor;

                // 4) Burbujita (pill)
                DrawTroopBubble(sb, pixel, font, screen, troops, bg);
            }
        }

        /// <summary>
        /// Permite limpiar posiciones cacheadas si cambiaste máscaras o mapa.
        /// </summary>
        public void InvalidateCache() => _centroidCache.Clear();

        // ================= helpers =================

        private static bool IsGray(Color c)
        {
            const int DELTA_GRAY = 12;
            const int MIN_LUMA = 24;
            const int MAX_LUMA = 240;
            int maxC = Math.Max(c.R, Math.Max(c.G, c.B));
            int minC = Math.Min(c.R, Math.Min(c.G, c.B));
            int luma = (c.R + c.G + c.B) / 3;
            return (maxC - minC) <= DELTA_GRAY && luma >= MIN_LUMA && luma <= MAX_LUMA && c.A > 10;
        }

        /// <summary>
        /// Centroid promedio de los píxeles grises (el “territorio”) en coords de textura.
        /// Se llama solo una vez por territorio y se cachea.
        /// </summary>
        private static Point ComputeMaskCentroid(Texture2D mask)
        {
            var w = mask.Width;
            var h = mask.Height;
            var data = new Color[w * h];
            mask.GetData(data);

            long sx = 0, sy = 0, n = 0;

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = data[row + x];
                    if (IsGray(c))
                    {
                        sx += x;
                        sy += y;
                        n++;
                    }
                }
            }

            if (n == 0) return new Point(w / 2, h / 2); // fallback
            return new Point((int)(sx / n), (int)(sy / n));
        }

        /// <summary>
        /// Dibuja una burbuja tipo “pill” con sombra, borde y texto centrado.
        /// </summary>
        private static void DrawTroopBubble(SpriteBatch sb, Texture2D px, SpriteFont? font, Vector2 center, int troops, Color bg)
        {
            // Medir el texto
            string txt = troops.ToString();
            Vector2 sz = font != null ? font.MeasureString(txt) : new Vector2(16, 16);

            // Margen interno y radio para puntas redondeadas
            int padX = 10, padY = 4;
            int radius = 10;

            int w = (int)Math.Ceiling(sz.X) + padX * 2;
            int h = (int)Math.Ceiling(sz.Y) + padY * 2;

            var rect = new Rectangle(
                (int)(center.X - w / 2f),
                (int)(center.Y - h / 2f),
                w,
                h
            );

            // Sombra
            sb.Draw(px, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), BubbleShadow);

            // Cuerpo (pill “barato”: rect central + 2 semicirculos aproximados con rects)
            // Rect central
            var body = new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, rect.Height);
            sb.Draw(px, body, bg);

            // “semicírculos” laterales aproximados con rectángulos verticales decrecientes
            for (int i = 0; i < radius; i++)
            {
                int hh = (int)(Math.Sqrt(radius * radius - i * i) * 2);
                int y = rect.Y + (rect.Height - hh) / 2;

                // izquierda
                sb.Draw(px, new Rectangle(rect.X + i, y, 1, hh), bg);
                // derecha
                sb.Draw(px, new Rectangle(rect.Right - 1 - i, y, 1, hh), bg);
            }

            // Borde
            DrawRoundedBorder(sb, px, rect, radius, BubbleBorder);

            // Texto
            if (font != null)
            {
                var pos = new Vector2(
                    rect.X + (rect.Width - sz.X) * 0.5f,
                    rect.Y + (rect.Height - sz.Y) * 0.5f - 1 // pequeño ajuste óptico
                );
                sb.DrawString(font, txt, pos, TextColor);
            }
        }

        private static void DrawRoundedBorder(SpriteBatch sb, Texture2D px, Rectangle r, int radius, Color c)
        {
            // Bordes rectos
            sb.Draw(px, new Rectangle(r.X + radius, r.Y, r.Width - radius * 2, 1), c);
            sb.Draw(px, new Rectangle(r.X + radius, r.Bottom - 1, r.Width - radius * 2, 1), c);
            sb.Draw(px, new Rectangle(r.X, r.Y + radius, 1, r.Height - radius * 2), c);
            sb.Draw(px, new Rectangle(r.Right - 1, r.Y + radius, 1, r.Height - radius * 2), c);

            // Esquinas redondeadas (cuarto de círculo con puntos)
            for (int i = 0; i < radius; i++)
            {
                int y = (int)Math.Sqrt(radius * radius - i * i);

                // sup-izq
                sb.Draw(px, new Rectangle(r.X + radius - i, r.Y + radius - y, 1, 1), c);
                // sup-der
                sb.Draw(px, new Rectangle(r.Right - radius + i - 1, r.Y + radius - y, 1, 1), c);
                // inf-izq
                sb.Draw(px, new Rectangle(r.X + radius - i, r.Bottom - radius + y - 1, 1, 1), c);
                // inf-der
                sb.Draw(px, new Rectangle(r.Right - radius + i - 1, r.Bottom - radius + y - 1, 1, 1), c);
            }
        }
    }
}
