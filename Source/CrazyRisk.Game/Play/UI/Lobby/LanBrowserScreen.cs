#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CrazyRiskGame.Net.LAN;

namespace CrazyRiskGame.Play.UI.Lobby
{
    /// <summary>
    /// Pantalla simple para descubrir lobbies en LAN y elegir a cuál unirse.
    /// No gestiona la conexión: emite eventos para que el host (Juego.cs) la haga.
    /// </summary>
    public sealed class LanBrowserScreen
    {
        private readonly ILanLobbyService _lobby;
        private readonly List<LobbyBeacon> _found = new();
        private readonly List<Rectangle> _rowRects = new();
        private readonly Rectangle _headerRect;
        private readonly Rectangle _listRect;
        private readonly Rectangle _footerRect;

        private SpriteFont _font;
        private Texture2D _pixel;
        private MouseState _prevMouse;

        private string _status = "Listo";
        private bool _isDiscovering = false;
        private DateTime _lastDiscoverStart = DateTime.MinValue;

        public event Action? OnBack;
        public event Action<LobbyBeacon>? OnJoinSelected;

        // Área recomendada: un panel en el lado derecho o fullscreen
        public LanBrowserScreen(GraphicsDevice gd, ILanLobbyService lobby, SpriteFont font, Rectangle viewport)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _font = font ?? throw new ArgumentNullException(nameof(font));

            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.White });

            // Layout muy simple: header / lista / footer
            int headerH = 52;
            int footerH = 64;
            _headerRect = new Rectangle(viewport.X + 12, viewport.Y + 12, viewport.Width - 24, headerH);
            _listRect   = new Rectangle(viewport.X + 12, _headerRect.Bottom + 10, viewport.Width - 24, viewport.Height - headerH - footerH - 34);
            _footerRect = new Rectangle(viewport.X + 12, _listRect.Bottom + 10, viewport.Width - 24, footerH);
        }

        public void StartDiscovery(int timeoutMs = 3000)
        {
            if (_isDiscovering) return;
            _isDiscovering = true;
            _status = "Buscando lobbies...";
            _found.Clear();

            _lastDiscoverStart = DateTime.UtcNow;

            // fire & forget (UI polling). Maneja excepción internamente vía eventos del servicio.
            _ = _lobby.DiscoverAsync(timeoutMs, b =>
            {
                lock (_found) { _found.Add(b); }
            }).ContinueWith(_ =>
            {
                _isDiscovering = false;
                _status = _found.Count == 0 ? "No se encontraron lobbies" : $"Encontrados: {_found.Count}";
                BuildRowRects();
            });
        }

        public void Update(GameTime gameTime)
        {
            var mouse = Mouse.GetState();
            var pos = mouse.Position;

            // Click en filas
            if (_listRect.Contains(pos) && mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                for (int i = 0; i < _rowRects.Count && i < _found.Count; i++)
                {
                    if (_rowRects[i].Contains(pos))
                    {
                        var sel = _found[i];
                        _status = $"Seleccionado: {sel.Name} ({sel.Address}:{sel.Port})";
                        OnJoinSelected?.Invoke(sel);
                        break;
                    }
                }
            }

            // Footer: botones “Buscar” y “Volver”
            var btnBuscar = ButtonRect(_footerRect, left: true);
            var btnVolver = ButtonRect(_footerRect, left: false);

            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                if (btnBuscar.Contains(pos))
                {
                    StartDiscovery();
                }
                else if (btnVolver.Contains(pos))
                {
                    OnBack?.Invoke();
                }
            }

            _prevMouse = mouse;
        }

        public void Draw(SpriteBatch sb)
        {
            // Header
            FillRect(sb, _headerRect, new Color(15, 15, 18, 220));
            StrokeRect(sb, _headerRect, new Color(255, 255, 255, 50));
            sb.DrawString(_font, "Unirse a lobby LAN", new Vector2(_headerRect.X + 8, _headerRect.Y + 10), Color.White);
            sb.DrawString(_font, _status, new Vector2(_headerRect.X + 8, _headerRect.Y + 28), new Color(200, 200, 200));

            // Lista
            FillRect(sb, _listRect, new Color(0, 0, 0, 100));
            StrokeRect(sb, _listRect, new Color(255, 255, 255, 50));

            int y = _listRect.Y + 6;
            int rowH = 36;
            int pad = 8;

            for (int i = 0; i < _found.Count; i++)
            {
                var row = new Rectangle(_listRect.X + 6, y, _listRect.Width - 12, rowH);
                var hover = row.Contains(Mouse.GetState().Position);
                FillRect(sb, row, hover ? new Color(255, 255, 255, 70) : new Color(255, 255, 255, 40));
                StrokeRect(sb, row, new Color(0, 0, 0, 180));

                var b = _found[i];
                string line = $"{b.Name}   {b.Address}:{b.Port}";
                sb.DrawString(_font, line, new Vector2(row.X + pad, row.Y + (row.Height - _font.LineSpacing) / 2f), Color.Black);

                y += rowH + 6;
                if (y > _listRect.Bottom - rowH) break;
            }

            // Footer (botones)
            var btnBuscar = ButtonRect(_footerRect, left: true);
            var btnVolver = ButtonRect(_footerRect, left: false);

            DrawButton(sb, btnBuscar, "Buscar");
            DrawButton(sb, btnVolver, "Volver");
        }

        // === helpers de UI ===
        private void BuildRowRects()
        {
            _rowRects.Clear();
            int y = _listRect.Y + 6;
            int rowH = 36;
            for (int i = 0; i < _found.Count; i++)
            {
                var r = new Rectangle(_listRect.X + 6, y, _listRect.Width - 12, rowH);
                _rowRects.Add(r);
                y += rowH + 6;
                if (y > _listRect.Bottom - rowH) break;
            }
        }

        private Rectangle ButtonRect(Rectangle host, bool left)
        {
            int w = 160, h = 40, gap = 12;
            int y = host.Y + (host.Height - h) / 2;
            if (left) return new Rectangle(host.X + gap, y, w, h);
            else return new Rectangle(host.Right - w - gap, y, w, h);
    
        }

        private void DrawButton(SpriteBatch sb, Rectangle r, string text)
        {
            bool hov = r.Contains(Mouse.GetState().Position);
            FillRect(sb, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0, 0, 0, 70));
            FillRect(sb, r, hov ? new Color(255, 255, 255, 220) : new Color(255, 255, 255, 180));
            StrokeRect(sb, r, new Color(0, 0, 0, 190), 2);

            var s = _font.MeasureString(text);
            sb.DrawString(_font, text, new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f), Color.Black);
        }

        private void FillRect(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_pixel, r, c);

        private void StrokeRect(SpriteBatch sb, Rectangle r, Color c, int th = 1)
        {
            sb.Draw(_pixel, new Rectangle(r.Left, r.Top, r.Width, th), c);
            sb.Draw(_pixel, new Rectangle(r.Left, r.Bottom - th, r.Width, th), c);
            sb.Draw(_pixel, new Rectangle(r.Left, r.Top, th, r.Height), c);
            sb.Draw(_pixel, new Rectangle(r.Right - th, r.Top, th, r.Height), c);
        }
    }
}