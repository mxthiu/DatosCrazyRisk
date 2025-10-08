#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CrazyRiskGame.Net.Lan; // <- coincide con tu LanLobby.cs

namespace CrazyRiskGame.Play.UI.Lobby
{
    /// <summary>
    /// Lobby LAN con selección de avatar, Ready y Start (host).
    /// </summary>
    public sealed class LanLobbyScreen
    {
        // --------- deps render -----------
        private readonly GraphicsDevice gd;
        private readonly SpriteFont? font;
        private readonly Texture2D pixel;
        private readonly List<Texture2D> avatarTex;

        // --------- red -----------
        private readonly LanLobby lobby;

        public enum Mode { Host, Client }
        private readonly Mode mode;

        // --------- input -----------
        private MouseState prevMouse;
        private KeyboardState prevKb;

        // --------- layout -----------
        private Rectangle panel;
        private Rectangle playersBox;
        private Rectangle avatarsBox;
        private Rectangle hostsBox;
        private Rectangle buttonsRow;
        private readonly List<Rectangle> avatarRects = new();

        // --------- botones -----------
        private struct Btn { public Rectangle Bounds; public string Text; public bool Hover; public bool Enabled; }
        private Btn btnReady, btnBack, btnStart, btnRefresh;

        // --------- estado UI -----------
        private int desiredAvatar = -1;
        private double hostsRefreshCooldown = 0;
        private int hostHoverIndex = -1;
        private List<DiscoveredHost> hosts = new();

        // --------- hooks opcionales -----------
        public bool BackRequested { get; private set; }

        public LanLobbyScreen(GraphicsDevice gd, SpriteFont? font, List<Texture2D> avatarTex, LanLobby lobby, Mode mode)
        {
            this.gd = gd;
            this.font = font;
            this.avatarTex = avatarTex;
            this.lobby = lobby;
            this.mode = mode;

            // pixel blanco
            pixel = new Texture2D(gd, 1, 1);
            pixel.SetData(new[] { Color.White });

            BuildLayout();
            RebuildAvatarGrid();
            BuildButtons();

            desiredAvatar = lobby.LocalAvatar; // -1 al inicio si no eligió
        }

        // ====================== Public API ======================
        public void Update(GameTime gt)
        {
            lobby.Update();

            var kb = Keyboard.GetState();
            var mouse = Mouse.GetState();
            var pos = mouse.Position;
            bool click = prevMouse.LeftButton == ButtonState.Released && mouse.LeftButton == ButtonState.Pressed;

            // Hovers
            btnReady.Hover   = btnReady.Bounds.Contains(pos);
            btnBack.Hover    = btnBack.Bounds.Contains(pos);
            btnStart.Hover   = btnStart.Bounds.Contains(pos);
            btnRefresh.Hover = btnRefresh.Bounds.Contains(pos);

            // READY (tecla R también)
            if (click && btnReady.Bounds.Contains(pos) && btnReady.Enabled
                || (kb.IsKeyDown(Keys.R) && !prevKb.IsKeyDown(Keys.R)))
            {
                lobby.SetReady(!lobby.LocalReady);
            }

            // BACK (tecla Escape)
            if (click && btnBack.Bounds.Contains(pos) && btnBack.Enabled
                || (kb.IsKeyDown(Keys.Escape) && !prevKb.IsKeyDown(Keys.Escape)))
            {
                BackRequested = true;
            }

            // START (solo host y cuando todos listos)
            btnStart.Enabled = (mode == Mode.Host) && lobby.State.AllReady();
            if (mode == Mode.Host && click && btnStart.Bounds.Contains(pos) && btnStart.Enabled)
            {
                lobby.HostStartGame();
            }

            // Avatares: click para pedir
            if (click)
            {
                for (int i = 0; i < avatarRects.Count; i++)
                {
                    if (avatarRects[i].Contains(pos))
                    {
                        desiredAvatar = i;
                        lobby.RequestAvatar(desiredAvatar);
                        break;
                    }
                }
            }

            // Cliente: descubrir/seleccionar host
            if (mode == Mode.Client)
            {
                hostsRefreshCooldown = Math.Max(0, hostsRefreshCooldown - gt.ElapsedGameTime.TotalSeconds);

                // Hover fila
                hostHoverIndex = -1;
                if (hostsBox.Contains(pos))
                {
                    int rowH = 26;
                    int maxRows = Math.Max(0, (hostsBox.Height - 38) / rowH);
                    for (int i = 0; i < Math.Min(maxRows, hosts.Count); i++)
                    {
                        var r = HostRowRect(i, rowH);
                        if (r.Contains(pos)) { hostHoverIndex = i; break; }
                    }
                }

                // Click fila -> Join
                if (click && hostHoverIndex >= 0 && hostHoverIndex < hosts.Count)
                {
                    var h = hosts[hostHoverIndex];
                    if (desiredAvatar < 0)
                        desiredAvatar = FirstFreeAvatar(h.LockedAvatars, avatarTex.Count);
                    lobby.JoinHost(h.EndPoint, desiredAvatar);
                }

                // Botón refrescar
                btnRefresh.Enabled = hostsRefreshCooldown <= 0;
                if (click && btnRefresh.Bounds.Contains(pos) && btnRefresh.Enabled)
                {
                    hosts = lobby.DiscoverHosts(400);
                    hostsRefreshCooldown = 0.5;
                }
            }

            prevMouse = mouse;
            prevKb = kb;
        }

        public void Draw(SpriteBatch sb)
        {
            // panel
            Fill(sb, panel, new Color(15, 15, 18, 235));
            Stroke(sb, panel, new Color(255,255,255,60), 1);

            // título
            if (font != null)
            {
                string title = mode == Mode.Host ? "LOBBY (HOST)" : "LOBBY (CLIENTE)";
                sb.DrawString(font, title, new Vector2(panel.X + 12, panel.Y + 10), Color.White);
            }

            DrawPlayers(sb);
            DrawAvatars(sb);

            if (mode == Mode.Client)
                DrawHosts(sb);

            // botones
            DrawButton(sb, ref btnReady, lobby.LocalReady ? "No listo" : "Listo");
            DrawButton(sb, ref btnBack, "Volver");

            if (mode == Mode.Host)
                DrawButton(sb, ref btnStart, lobby.State.AllReady() ? "Iniciar partida" : "Esperando a todos...");
            else
                DrawButton(sb, ref btnRefresh, "Buscar hosts");
        }

        public void LeaveLobby() => lobby.Leave();

        // Exponer para Juego.cs si quiere leer estado (p.ej. GameStarting)
        public LanLobby Lobby => lobby;

        // ====================== UI internals ======================
        private void BuildLayout()
        {
            int W = gd.PresentationParameters.BackBufferWidth;
            int H = gd.PresentationParameters.BackBufferHeight;

            panel      = new Rectangle((int)(W*0.07f), (int)(H*0.07f), (int)(W*0.86f), (int)(H*0.86f));
            playersBox = new Rectangle(panel.X + 14, panel.Y + 46, (int)(panel.Width * 0.34f), (int)(panel.Height * 0.50f));
            avatarsBox = new Rectangle(playersBox.Right + 14, playersBox.Y, (int)(panel.Width * 0.40f), playersBox.Height);

            if (mode == Mode.Client)
                hostsBox = new Rectangle(panel.X + 14, playersBox.Bottom + 12, playersBox.Width, panel.Bottom - playersBox.Bottom - 20);
            else
                hostsBox = Rectangle.Empty;

            buttonsRow = new Rectangle(avatarsBox.Right + 14, playersBox.Bottom + 12, panel.Right - avatarsBox.Right - 28, mode == Mode.Client ? hostsBox.Height : (panel.Bottom - playersBox.Bottom - 20));
        }

        private void RebuildAvatarGrid()
        {
            avatarRects.Clear();

            // 3x2
            int cols = 3, rows = 2, gap = 12;
            int cellW = (avatarsBox.Width - (cols + 1) * gap) / cols;
            int cellH = (avatarsBox.Height - (rows + 1) * gap) / rows;
            int x0 = avatarsBox.X + gap, y0 = avatarsBox.Y + gap;

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                avatarRects.Add(new Rectangle(x0 + c * (cellW + gap), y0 + r * (cellH + gap), cellW, cellH));
            }
        }

        private void BuildButtons()
        {
            int bw = 180, bh = 46, gap = 12;
            int y = buttonsRow.Bottom - bh;
            int x2 = buttonsRow.Right - bw;               // derecha
            int x1 = x2 - (bw + gap);                     // centro
            int x0 = x1 - (bw + gap);                     // izquierda

            btnReady   = new Btn { Bounds = new Rectangle(x0, y, bw, bh), Enabled = true, Text = "Listo" };
            btnBack    = new Btn { Bounds = new Rectangle(x1, y, bw, bh), Enabled = true, Text = "Volver" };
            btnStart   = new Btn { Bounds = new Rectangle(x2, y, bw, bh), Enabled = false, Text = "Iniciar partida" };
            btnRefresh = new Btn { Bounds = new Rectangle(x2, y, bw, bh), Enabled = true, Text = "Buscar hosts" };
        }

        private void DrawPlayers(SpriteBatch sb)
        {
            Fill(sb, playersBox, new Color(255,255,255,16));
            Stroke(sb, playersBox, new Color(255,255,255,60), 1);

            if (font == null) return;

            var st = lobby.State;
            int y = playersBox.Y + 8;
            sb.DrawString(font, $"Host: {st.HostName ?? "Host"}", new Vector2(playersBox.X + 8, y), Color.White);
            y += 26;

            for (int i = 0; i < st.Players.Length; i++)
            {
                var p = st.Players[i];
                string name = p.IsOccupied ? p.Name : "(vacío)";
                string tag = p.IsHost ? "HOST" : "CLI";
                string ready = p.Ready ? "Listo" : "No listo";
                string line = $"{i}: {name} [{tag}] Av:{(p.AvatarId >= 0 ? p.AvatarId.ToString() : "-")}  {ready}";
                sb.DrawString(font, line, new Vector2(playersBox.X + 8, y), Color.White);
                y += 22;
            }

            if (st.AllReady())
                sb.DrawString(font, "¡Todos listos!", new Vector2(playersBox.X + 8, y + 6), new Color(0,255,0));
        }

        private void DrawAvatars(SpriteBatch sb)
        {
            Fill(sb, avatarsBox, new Color(255,255,255,16));
            Stroke(sb, avatarsBox, new Color(255,255,255,60), 1);

            if (font != null)
                sb.DrawString(font, "Selecciona tu avatar (clic)", new Vector2(avatarsBox.X + 8, avatarsBox.Y + 6), Color.White);

            for (int i = 0; i < avatarRects.Count; i++)
            {
                var r = avatarRects[i];
                bool locked = AvatarTaken(i);

                // fondo
                Fill(sb, r, locked ? new Color(70,70,70,210) : new Color(0,0,0,120));
                Stroke(sb, r, Color.White, 2);

                // retrato / índice
                if (i < avatarTex.Count)
                {
                    var tex = avatarTex[i];
                    float sc = MathF.Min((float)r.Width / tex.Width, (float)r.Height / tex.Height) * 0.9f;
                    var pos = new Vector2(r.X + (r.Width - tex.Width * sc) * 0.5f, r.Y + (r.Height - tex.Height * sc) * 0.5f);
                    sb.Draw(tex, pos, null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                }
                else if (font != null)
                {
                    sb.DrawString(font, i.ToString(), new Vector2(r.X + 8, r.Y + 6), Color.White);
                }

                // enmarcados
                if (lobby.LocalAvatar == i) // confirmado por host
                    Stroke(sb, new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), new Color(0,255,0), 3);
                else if (desiredAvatar == i && desiredAvatar != lobby.LocalAvatar) // pedido pendiente
                    Stroke(sb, new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), new Color(255,255,0), 3);

                if (locked)
                {
                    Fill(sb, r, new Color(0,0,0,110));
                    if (font != null) sb.DrawString(font, "Ocupado", new Vector2(r.X + 6, r.Bottom - 22), Color.White);
                }
            }
        }

        private void DrawHosts(SpriteBatch sb)
        {
            Fill(sb, hostsBox, new Color(255,255,255,16));
            Stroke(sb, hostsBox, new Color(255,255,255,60), 1);

            if (font == null) return;

            int rowH = 26;
            int x = hostsBox.X + 8;
            int y = hostsBox.Y + 8;

            sb.DrawString(font, "Hosts LAN (clic para unirse):", new Vector2(x, y), Color.White);
            y += 24;

            int maxRows = Math.Max(0, (hostsBox.Height - (y - hostsBox.Y) - 8) / rowH);
            for (int i = 0; i < Math.Min(maxRows, hosts.Count); i++)
            {
                var r = HostRowRect(i, rowH, y);
                bool hov = (hostHoverIndex == i);
                Fill(sb, r, hov ? new Color(255,255,255,60) : new Color(255,255,255,24));
                Stroke(sb, r, new Color(0,0,0,160), 1);

                var h = hosts[i];
                string txt = $"{h.HostName} @ {h.EndPoint.Address} ({h.PlayerCount}/3)";
                sb.DrawString(font, txt, new Vector2(r.X + 6, r.Y + 4), Color.White);
            }
        }

        private void DrawButton(SpriteBatch sb, ref Btn b, string? overrideText = null)
        {
            string text = overrideText ?? b.Text;
            var bg = b.Enabled ? (b.Hover ? new Color(255,255,255,220) : new Color(255,255,255,180))
                               : new Color(200,200,200,120);
            // sombra + cuerpo
            Fill(sb, new Rectangle(b.Bounds.X + 3, b.Bounds.Y + 3, b.Bounds.Width, b.Bounds.Height), new Color(0,0,0,70));
            Fill(sb, b.Bounds, bg);
            Stroke(sb, b.Bounds, new Color(0,0,0,190), 2);

            if (font != null)
            {
                var s = font.MeasureString(text);
                sb.DrawString(font, text, new Vector2(b.Bounds.X + (b.Bounds.Width - s.X) * 0.5f,
                                                      b.Bounds.Y + (b.Bounds.Height - s.Y) * 0.5f), Color.Black);
            }
        }

        // ====================== util ======================
        private bool AvatarTaken(int idx)
        {
            var st = lobby.State;
            for (int i = 0; i < st.Players.Length; i++)
                if (st.Players[i].IsOccupied && st.Players[i].AvatarId == idx)
                    return true;
            return false;
        }

        private static int FirstFreeAvatar(int[] locked, int count)
        {
            var set = new HashSet<int>(locked ?? Array.Empty<int>());
            for (int i = 0; i < count; i++) if (!set.Contains(i)) return i;
            return -1;
        }

        private Rectangle HostRowRect(int i, int rowH, int y0 = -1)
        {
            if (y0 < 0) y0 = hostsBox.Y + 8 + 24;
            return new Rectangle(hostsBox.X + 8, y0 + i * rowH, hostsBox.Width - 16, rowH - 4);
        }

        private void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(pixel, r, c);
        private void Stroke(SpriteBatch sb, Rectangle r, Color c, int t)
        {
            sb.Draw(pixel, new Rectangle(r.Left, r.Top, r.Width, t), c);
            sb.Draw(pixel, new Rectangle(r.Left, r.Bottom - t, r.Width, t), c);
            sb.Draw(pixel, new Rectangle(r.Left, r.Top, t, r.Height), c);
            sb.Draw(pixel, new Rectangle(r.Right - t, r.Top, t, r.Height), c);
        }
    }
}