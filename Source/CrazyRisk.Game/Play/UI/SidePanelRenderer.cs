#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Panel lateral con pestañas (tabs) y botón de colapso.
    /// - No toca la lógica del juego; solo UI.
    /// - Expone hit-testing para saber si se clicó un tab o el botón de colapsar.
    /// - Mantiene "ActiveTabIndex" y "Collapsed".
    /// - Entrega el área de contenido vía GetContentArea().
    /// </summary>
    public sealed class SidePanelRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _pixel;

        // Área del panel completo y header
        private Rectangle _panelRect;
        private Rectangle _headerRect;

        // Botón de colapsar (pestaña vertical a la izquierda)
        private Rectangle _collapseButtonRect;
        private string _collapseButtonText = "<";

        // Tabs
        private readonly List<TabItem> _tabs = new();
        private int _activeTabIndex = 0;

        // Apariencia
        private readonly Color _panelBg = new Color(15, 15, 18, 225);
        private readonly Color _headerBg = new Color(255, 255, 255, 20);
        private readonly Color _headerBorder = new Color(255, 255, 255, 60);
        private readonly Color _tabActiveBg = new Color(255, 255, 255, 220);
        private readonly Color _tabHoverBg  = new Color(255, 255, 255, 160);
        private readonly Color _tabBg       = new Color(255, 255, 255, 110);
        private readonly Color _tabBorder   = new Color(0, 0, 0, 200);

        // Layout
        private const int HEADER_H = 36;
        private const int TAB_W = 96;
        private const int TAB_GAP = 6;
        private const int PANEL_INSET = 10;
        private const int COLLAPSE_W = 24;

        public bool Collapsed { get; private set; }

        private struct TabItem
        {
            public Rectangle Bounds;
            public string Text;
            public bool Hover;
        }

        public SidePanelRenderer(SpriteFont font, Texture2D pixel)
        {
            _font  = font  ?? throw new ArgumentNullException(nameof(font));
            _pixel = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        /// <summary>
        /// Define el rectángulo del panel lateral (completo). Calcula header y botón de colapsar.
        /// </summary>
        public void SetPanelArea(Rectangle panelRect)
        {
            _panelRect = panelRect;
            _headerRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, HEADER_H);
            _collapseButtonRect = new Rectangle(panelRect.X - COLLAPSE_W, panelRect.Y + 8, COLLAPSE_W, 28);
            LayoutTabs(); // reposiciona tabs
        }

        /// <summary>
        /// Establece el set de pestañas (texto visible). Por defecto activa el índice 0.
        /// </summary>
        public void SetTabs(params string[] tabTitles)
        {
            _tabs.Clear();
            for (int i = 0; i < tabTitles.Length; i++)
            {
                _tabs.Add(new TabItem { Text = tabTitles[i] });
            }
            _activeTabIndex = Math.Clamp(_activeTabIndex, 0, Math.Max(0, _tabs.Count - 1));
            LayoutTabs();
        }

        /// <summary>
        /// Índice de pestaña activa.
        /// </summary>
        public int ActiveTabIndex
        {
            get => _activeTabIndex;
            set => _activeTabIndex = Math.Clamp(value, 0, Math.Max(0, _tabs.Count - 1));
        }

        /// <summary>
        /// Actualiza hovers. No hace cambios de estado (ni colapsa ni cambia tab).
        /// </summary>
        public void UpdateHover(Point mousePos)
        {
            // Hover botón colapsar
            // (El dibujo usa su propio estado, aquí no almacenamos hover del botón, basta con hit-test en Draw si hace falta)
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                t.Hover = t.Bounds.Contains(mousePos);
                _tabs[i] = t;
            }
        }

        /// <summary>
        /// Alterna el colapso si se clicó el botón. Devuelve true si se toggled.
        /// </summary>
        public bool TryToggleCollapse(Point mousePos)
        {
            if (_collapseButtonRect.Contains(mousePos))
            {
                Collapsed = !Collapsed;
                _collapseButtonText = Collapsed ? ">" : "<";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Si se clicó algún tab, devuelve su índice; si no, -1. No cambia ActiveTabIndex (tú decides).
        /// </summary>
        public int HitTestTabClick(Point mousePos)
        {
            if (Collapsed) return -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Bounds.Contains(mousePos))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Devuelve el área disponible para contenido, dentro del panel (bajo el header, con padding).
        /// Si el panel está colapsado, devuelve Rectangle.Empty.
        /// </summary>
        public Rectangle GetContentArea()
        {
            if (Collapsed || _panelRect.Width <= 0 || _panelRect.Height <= 0)
                return Rectangle.Empty;

            return new Rectangle(
                _panelRect.X + PANEL_INSET,
                _panelRect.Y + HEADER_H + PANEL_INSET,
                _panelRect.Width - PANEL_INSET * 2,
                _panelRect.Height - HEADER_H - PANEL_INSET * 2
            );
        }

        /// <summary>
        /// Dibuja el panel, header, tabs y el botón de colapsar.
        /// No dibuja contenido; usa GetContentArea() y tu propio renderer para cada pestaña.
        /// </summary>
        public void Draw(SpriteBatch sb)
        {
            // Botón colapsar (pestaña lateral)
            var colBg = new Color(255, 255, 255, 180);
            sb.Draw(_pixel, _collapseButtonRect, colBg);
            DrawRect(sb, _collapseButtonRect, new Color(0, 0, 0, 200), 2);
            var ts = _font.MeasureString(_collapseButtonText);
            sb.DrawString(_font,
                _collapseButtonText,
                new Vector2(
                    _collapseButtonRect.X + (_collapseButtonRect.Width - ts.X) * 0.5f,
                    _collapseButtonRect.Y + (_collapseButtonRect.Height - ts.Y) * 0.5f
                ),
                Color.Black
            );

            if (Collapsed) return;

            // Panel
            sb.Draw(_pixel, _panelRect, _panelBg);
            DrawRect(sb, _panelRect, new Color(255, 255, 255, 40), 1);

            // Header
            sb.Draw(_pixel, _headerRect, _headerBg);
            DrawRect(sb, _headerRect, _headerBorder, 1);

            // Tabs
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                bool active = (i == _activeTabIndex);
                var bgc = active ? _tabActiveBg : (tab.Hover ? _tabHoverBg : _tabBg);

                sb.Draw(_pixel, tab.Bounds, bgc);
                DrawRect(sb, tab.Bounds, _tabBorder, active ? 2 : 1);

                var s = _font.MeasureString(tab.Text);
                var tx = tab.Bounds.X + (tab.Bounds.Width - s.X) * 0.5f;
                var ty = tab.Bounds.Y + (tab.Bounds.Height - s.Y) * 0.5f;
                sb.DrawString(_font, tab.Text, new Vector2(tx, ty), Color.Black);
            }

            // (El contenido lo pinta el caller con GetContentArea()).
        }

        private void LayoutTabs()
        {
            if (_tabs.Count == 0 || _panelRect.Width <= 0) return;

            int x = _panelRect.X + 8;
            int y = _headerRect.Y + 4;
            int h = _headerRect.Height - 8;

            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                t.Bounds = new Rectangle(x, y, TAB_W, h);
                t.Hover = false;
                _tabs[i] = t;

                x += TAB_W + TAB_GAP;
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
