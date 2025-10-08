#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CrazyRiskGame.Play.Net;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Pantalla de Lobby LAN. Funciona en dos modos:
    ///  - Host: crea lobby y espera clientes.
    ///  - Client: descubre hosts, se une, elige avatar y marca Listo.
    ///
    /// Esta pantalla NO navega por sí sola. Exponerás su ciclo desde Juego.cs:
    ///   var screen = new LanLobbyScreen(graphicsDevice, font, LanLobbyScreen.Mode.Host /*o Client*/);
    ///   screen.Open();
    ///   en Update(): screen.Update(gameTime);
    ///   en Draw():   screen.Draw(spriteBatch, viewportRect);
    ///   si (screen.Result == ScreenResult.StartGame) { ... }
    ///   si (screen.Result == ScreenResult.Back) { ... }
    ///
    /// Requiere LanLobby.cs (Play/Net/LanLobby.cs).
    /// </summary>
    public sealed class LanLobbyScreen : IDisposable
    {
        // ==================== API pública mínima ====================
        public enum Mode { Host, Client }
        public enum ScreenResult { None, Back, StartGame }

        public ScreenResult Result { get; private set; } = ScreenResult.None;
        public Mode CurrentMode { get; private set; }
        public LanLobby Lobby { get; } = new LanLobby();
        public bool IsOpen { get; private set; }

        public string LocalName
        {
            get => _displayName;
            set => _displayName = string.IsNullOrWhiteSpace(value) ? _displayName : value.Trim();
        }

        public int AvatarsCount { get; set; } = 6; // cuántos avatares existen en tu juego (para bloquearlos)

        // ==================== UI / MonoGame ====================
        private readonly GraphicsDevice _gd;
        private readonly SpriteFont _font;
        private readonly Texture2D _px;
        private Rectangle _viewport;

        // UI state
        private MouseState _prevMouse;
        private KeyboardState _prevKb;

        private string _displayName = $"Player-{Environment.MachineName}";
        private string _hostName    = "Host";

        private int _localAvatar = -1;
        private bool _localReady = false;

        // Client discovery
        private List<DiscoveredHost> _foundHosts = new();
        private int _hostSelectedIndex = -1;

        // Botones simples
        private struct Btn { public Rectangle R; public string T; public bool H; public bool Enabled; }
        private readonly List<Btn> _buttons = new();

        // Layout
        private Rectangle _header, _left, _right, _footer;

        public LanLobbyScreen(GraphicsDevice graphicsDevice, SpriteFont font, Mode initialMode)
        {
            _gd = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _font = font ?? throw new ArgumentNullException(nameof(font));
            CurrentMode = initialMode;

            _px = new Texture2D(_gd, 1, 1);
            _px.SetData(new[] { Color.White });
        }

        public void Open()
        {
            if (IsOpen) return;

            // Limpia estado momentáneo
            Result = ScreenResult.None;
            _foundHosts.Clear();
            _hostSelectedIndex = -1;
            _localReady = false;
            _localAvatar = -1;

            // Arranca el lobby según modo
            if (CurrentMode == Mode.Host)
            {
                Lobby.StartHost(_hostName);
                Lobby.SetReady(false);
            }
            else
            {
                Lobby.StartClient(_displayName);
            }

            IsOpen = true;
            RebuildButtons();
        }

        public void Close()
        {
            if (!IsOpen) return;
            Lobby.Leave();
            IsOpen = false;
        }

        public void Dispose()
        {
            try { Close(); } catch { }
            try { _px.Dispose(); } catch { }
        }

        // ==================== Update/Draw ====================
        public void Update(GameTime gameTime, Rectangle viewport)
        {
            _viewport = viewport;
            Layout(viewport);
            var kb = Keyboard.GetState();
            var ms = Mouse.GetState();

            if (!IsOpen) return;

            // ESC → volver
            if (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape))
            {
                Result = ScreenResult.Back;
                Close();
                return;
            }

            // Lobby tick (host hace broadcast, timeout, etc.)
            Lobby.Update();

            // Mouse hover en botones
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                b.H = b.R.Contains(ms.Position);
                _buttons[i] = b;
            }

            // Click
            bool clicked = _prevMouse.LeftButton == ButtonState.Released && ms.LeftButton == ButtonState.Pressed;
            if (clicked)
            {
                for (int i = 0; i < _buttons.Count; i++)
                {
                    var b = _buttons[i];
                    if (!b.Enabled) continue;
                    if (!b.R.Contains(ms.Position)) continue;

                    HandleButton(b.T);
                    break;
                }

                // Click sobre lista de hosts (modo client)
                if (CurrentMode == Mode.Client && _right.Contains(ms.Position))
                {
                    // items
                    int y = _right.Y + 64;
                    int h = 40;
                    for (int idx = 0; idx < _foundHosts.Count; idx++)
                    {
                        var itemR = new Rectangle(_right.X + 10, y, _right.Width - 20, h);
                        if (itemR.Contains(ms.Position))
                        {
                            _hostSelectedIndex = idx;
                            break;
                        }
                        y += h + 6;
                    }
                }

                // Click sobre selección de avatar (modo host y client)
                // Host: avatar del host (slot 0)
                // Client: avatar local
                var aSel = new Rectangle(_left.X + 10, _left.Bottom - 100, _left.Width - 20, 32);
                if (aSel.Contains(ms.Position))
                {
                    // dividir en [<] [#] [>]
                    int w = aSel.Width;
                    var leftR  = new Rectangle(aSel.X, aSel.Y, 32, aSel.Height);
                    var rightR = new Rectangle(aSel.Right - 32, aSel.Y, 32, aSel.Height);

                    if (leftR.Contains(ms.Position))
                    {
                        ChangeLocalAvatar(-1);
                    }
                    else if (rightR.Contains(ms.Position))
                    {
                        ChangeLocalAvatar(+1);
                    }
                }
            }

            _prevKb = kb;
            _prevMouse = ms;
        }

        public void Draw(SpriteBatch sb, Rectangle viewport)
        {
            if (!IsOpen) return;

            // Paneles base
            sb.Draw(_px, _header, new Color(20, 22, 26, 255));
            sb.Draw(_px, _left,   new Color(255, 255, 255, 14));
            sb.Draw(_px, _right,  new Color(255, 255, 255, 10));
            sb.Draw(_px, _footer, new Color(20, 22, 26, 255));

            DrawOutline(sb, _header, Color.White * 0.25f);
            DrawOutline(sb, _left,   Color.White * 0.25f);
            DrawOutline(sb, _right,  Color.White * 0.25f);
            DrawOutline(sb, _footer, Color.White * 0.25f);

            // Header
            var title = CurrentMode == Mode.Host ? "Lobby LAN (Host)" : "Lobby LAN (Cliente)";
            sb.DrawString(_font, title, new Vector2(_header.X + 12, _header.Y + 10), Color.White);

            // Left: estado de jugadores
            DrawPlayers(sb);

            // Right: discovery o ayuda
            if (CurrentMode == Mode.Client)
                DrawDiscovery(sb);
            else
                DrawHelpHost(sb);

            // Footer: botones
            foreach (var b in _buttons)
                DrawButton(sb, b);
        }

        // ==================== Botonera / Acciones ====================
        private void HandleButton(string key)
        {
            switch (key)
            {
                case "BACK":
                    Result = ScreenResult.Back;
                    Close();
                    break;

                case "DISCOVER":
                    if (CurrentMode == Mode.Client)
                    {
                        _foundHosts = Lobby.DiscoverHosts();
                        _hostSelectedIndex = _foundHosts.Count > 0 ? 0 : -1;
                    }
                    break;

                case "JOIN":
                    if (CurrentMode == Mode.Client && _hostSelectedIndex >= 0 && _hostSelectedIndex < _foundHosts.Count)
                    {
                        // avatar elegido (si -1 tomará cualquiera)
                        Lobby.JoinHost(_foundHosts[_hostSelectedIndex].EndPoint, _localAvatar);
                    }
                    break;

                case "LEAVE":
                    Lobby.Leave();
                    // re-abrir cliente para seguir buscando sin salir de pantalla
                    if (CurrentMode == Mode.Client)
                        Lobby.StartClient(_displayName);
                    else
                        Lobby.StartHost(_hostName);
                    break;

                case "READY":
                    _localReady = !_localReady;
                    Lobby.SetReady(_localReady);
                    break;

                case "STARTGAME":
                    // Solo host y si all ready
                    if (CurrentMode == Mode.Host && Lobby.HostStartGame())
                    {
                        Result = ScreenResult.StartGame;
                        // No cerramos el lobby aquí; el juego puede mantenerlo
                    }
                    break;
            }

            RebuildButtons();
        }

        private void ChangeLocalAvatar(int delta)
        {
            if (AvatarsCount <= 0) return;

            // Ciclar avatar local y pedir cambio
            int next = _localAvatar;
            if (next < 0) next = 0;
            else next = (next + delta + AvatarsCount) % AvatarsCount;

            // Ver si está libre según el estado más reciente del lobby
            // Para el host: slot 0; para cliente: el host validará y responderá con State
            _localAvatar = next;

            if (CurrentMode == Mode.Client)
                Lobby.RequestAvatar(_localAvatar);
            else
            {
                // Host actualiza su avatar y hace broadcast en SetReady/Start o al tick
                // Haremos un pequeño truco: togglear Ready para forzar broadcast rápido de estado
                bool keep = _localReady;
                Lobby.SetReady(!keep);
                Lobby.SetReady(keep);
            }
        }

        private void RebuildButtons()
        {
            _buttons.Clear();

            // Back (siempre)
            _buttons.Add(MkBtn(_footer.Right - 120 - 12, _footer.Y + 10, 120, 40, "Volver", "BACK", true));

            if (CurrentMode == Mode.Client)
            {
                bool joined = Lobby.State.PlayerCount() > 0 && Lobby.State.Players[0].IsOccupied; // hay host y quizás yo dentro
                bool iAmInside = FindMySlot() >= 0;

                _buttons.Add(MkBtn(_right.X + 10, _right.Y + 10, 160, 40, "Buscar hosts", "DISCOVER", true));
                _buttons.Add(MkBtn(_right.X + 180, _right.Y + 10, 120, 40, "Unirse", "JOIN", _hostSelectedIndex >= 0));
                _buttons.Add(MkBtn(_right.X + 310, _right.Y + 10, 120, 40, "Salir lobby", "LEAVE", iAmInside || joined));

                _buttons.Add(MkBtn(_footer.X + 12, _footer.Y + 10, 160, 40, _localReady ? "Listo ✓" : "Listo", "READY", iAmInside));
            }
            else // Host
            {
                bool canStart = Lobby.State.AllReady();
                _buttons.Add(MkBtn(_footer.X + 12, _footer.Y + 10, 160, 40, _localReady ? "Listo ✓" : "Listo", "READY", true));
                _buttons.Add(MkBtn(_footer.X + 184, _footer.Y + 10, 180, 40, "Iniciar partida", "STARTGAME", canStart));
                _buttons.Add(MkBtn(_right.X + 10, _right.Y + 10, 120, 40, "Reiniciar", "LEAVE", true));
            }
        }

        private Btn MkBtn(int x, int y, int w, int h, string text, string key, bool enabled)
        {
            return new Btn
            {
                R = new Rectangle(x, y, w, h),
                T = key,
                H = false,
                Enabled = enabled
            };
        }

        // ==================== Dibujo ====================
        private void DrawPlayers(SpriteBatch sb)
        {
            // Panel título
            sb.DrawString(_font, "Jugadores", new Vector2(_left.X + 10, _left.Y + 10), Color.White);

            // Slots (0..2)
            int y = _left.Y + 42;
            for (int i = 0; i < Lobby.State.Players.Length; i++)
            {
                var p = Lobby.State.Players[i];
                var row = new Rectangle(_left.X + 10, y, _left.Width - 20, 44);
                sb.Draw(_px, row, new Color(255, 255, 255, 18));
                DrawOutline(sb, row, Color.White * 0.25f);

                string who = p.IsOccupied ? p.Name : "(vacío)";
                string sub = p.IsHost ? "Host" : "Cliente";
                string ready = p.IsOccupied ? (p.Ready ? "✓ Listo" : "—") : "";

                // Avatar bloqueado
                string avatar = p.AvatarId >= 0 ? $"Avatar {p.AvatarId}" : "(sin avatar)";
                var txt = $"{i}: {who} [{sub}]   {avatar}   {ready}";
                sb.DrawString(_font, txt, new Vector2(row.X + 8, row.Y + 12), Color.White);

                y += row.Height + 6;
            }

            // Selector de avatar local
            var sel = new Rectangle(_left.X + 10, _left.Bottom - 100, _left.Width - 20, 32);
            sb.Draw(_px, sel, new Color(255, 255, 255, 18));
            DrawOutline(sb, sel, Color.White * 0.25f);

            string label = CurrentMode == Mode.Host ? "Tu avatar (Host)" : "Tu avatar";
            var labSz = _font.MeasureString(label);
            sb.DrawString(_font, label, new Vector2(sel.X, sel.Y - labSz.Y - 4), Color.White);

            // [<]   [Avatar N o "auto"]   [>]
            DrawButtonSimple(sb, new Rectangle(sel.X, sel.Y, 32, sel.Height), "<", true);
            DrawButtonSimple(sb, new Rectangle(sel.Right - 32, sel.Y, 32, sel.Height), ">", true);

            string mid = _localAvatar >= 0 ? $"Avatar { _localAvatar }" : "(auto)";
            var midSz = _font.MeasureString(mid);
            sb.DrawString(_font, mid, new Vector2(sel.X + (sel.Width - midSz.X) * 0.5f, sel.Y + (sel.Height - midSz.Y) * 0.5f), Color.White);
        }

        private void DrawDiscovery(SpriteBatch sb)
        {
            // Botonera ya dibuja arriba; aquí la lista
            var title = "Hosts disponibles";
            sb.DrawString(_font, title, new Vector2(_right.X + 10, _right.Y + 56 - _font.LineSpacing), Color.White);

            int y = _right.Y + 64;
            int h = 40;
            for (int i = 0; i < _foundHosts.Count; i++)
            {
                var itemR = new Rectangle(_right.X + 10, y, _right.Width - 20, h);
                bool sel = i == _hostSelectedIndex;
                sb.Draw(_px, itemR, sel ? new Color(255, 255, 255, 40) : new Color(255, 255, 255, 18));
                DrawOutline(sb, itemR, Color.White * 0.25f);

                var host = _foundHosts[i];
                string line = host.ToString();
                sb.DrawString(_font, line, new Vector2(itemR.X + 8, itemR.Y + 10), Color.White);

                y += h + 6;
            }

            if (_foundHosts.Count == 0)
            {
                var msg = "(No hay hosts por ahora. Pulsa 'Buscar hosts')";
                sb.DrawString(_font, msg, new Vector2(_right.X + 10, y), Color.Gray);
            }
        }

        private void DrawHelpHost(SpriteBatch sb)
        {
            var helpR = new Rectangle(_right.X + 10, _right.Y + 10 + 48, _right.Width - 20, _right.Height - 68);
            sb.Draw(_px, helpR, new Color(255, 255, 255, 12));
            DrawOutline(sb, helpR, Color.White * 0.2f);

            string[] lines = new[]
            {
                "Este equipo es el Host.",
                "Los clientes deben estar en el mismo Wi-Fi.",
                "",
                "Estados:",
                " • Marca 'Listo' cuando estés preparado.",
                " • 'Iniciar partida' se habilita cuando todos están listos.",
                "",
                "Consejos:",
                " • Revisa el firewall si no te encuentran.",
                " • Evita redes con aislamiento cliente-cliente.",
            };

            int y = helpR.Y + 8;
            foreach (var ln in lines)
            {
                sb.DrawString(_font, ln, new Vector2(helpR.X + 8, y), Color.White);
                y += _font.LineSpacing + 2;
            }
        }

        private void DrawButton(SpriteBatch sb, Btn b)
        {
            var bg = b.Enabled
                ? (b.H ? new Color(255, 255, 255, 220) : new Color(255, 255, 255, 180))
                : new Color(255, 255, 255, 90);

            sb.Draw(_px, new Rectangle(b.R.X + 3, b.R.Y + 3, b.R.Width, b.R.Height), new Color(0, 0, 0, 60));
            sb.Draw(_px, b.R, bg);
            DrawOutline(sb, b.R, Color.Black * 0.85f, 2);

            string label = b.T switch
            {
                "BACK"       => "Volver",
                "DISCOVER"   => "Buscar hosts",
                "JOIN"       => "Unirse",
                "LEAVE"      => "Salir lobby",
                "READY"      => _localReady ? "Listo ✓" : "Listo",
                "STARTGAME"  => "Iniciar partida",
                _            => b.T
            };

            var s = _font.MeasureString(label);
            sb.DrawString(_font, label, new Vector2(b.R.X + (b.R.Width - s.X) * 0.5f, b.R.Y + (b.R.Height - s.Y) * 0.5f), Color.Black);
        }

        private void DrawButtonSimple(SpriteBatch sb, Rectangle r, string label, bool enabled)
        {
            var bg = enabled ? new Color(255, 255, 255, 160) : new Color(255, 255, 255, 90);
            sb.Draw(_px, r, bg);
            DrawOutline(sb, r, Color.Black * 0.8f, 1);

            var s = _font.MeasureString(label);
            sb.DrawString(_font, label, new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f), Color.Black);
        }

        private void DrawOutline(SpriteBatch sb, Rectangle r, Color color, int thick = 1)
        {
            sb.Draw(_px, new Rectangle(r.Left, r.Top, r.Width, thick), color);
            sb.Draw(_px, new Rectangle(r.Left, r.Bottom - thick, r.Width, thick), color);
            sb.Draw(_px, new Rectangle(r.Left, r.Top, thick, r.Height), color);
            sb.Draw(_px, new Rectangle(r.Right - thick, r.Top, thick, r.Height), color);
        }

        // ==================== Layout ====================
        private void Layout(Rectangle view)
        {
            // Header 64px, Footer 64px, resto dividido en 2 paneles
            _header = new Rectangle(view.X, view.Y, view.Width, 64);
            _footer = new Rectangle(view.X, view.Bottom - 64, view.Width, 64);

            int midY = _header.Bottom;
            int h = view.Height - _header.Height - _footer.Height;
            int leftW = (int)(view.Width * 0.56f);
            _left  = new Rectangle(view.X + 12, midY + 12, leftW - 24, h - 24);
            _right = new Rectangle(view.X + leftW + 12, midY + 12, view.Width - leftW - 24, h - 24);
        }

        // ==================== Util ====================
        private int FindMySlot()
        {
            // Aproximación: el local está “ocupado” por su nombre
            for (int i = 0; i < Lobby.State.Players.Length; i++)
            {
                if (Lobby.State.Players[i].IsOccupied && Lobby.State.Players[i].Name == _displayName)
                    return i;
                if (i == 0 && CurrentMode == Mode.Host && Lobby.State.Players[i].IsOccupied)
                    return 0; // host local
            }
            return -1;
        }
    }
}