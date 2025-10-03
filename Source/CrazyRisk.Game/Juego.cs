#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Audio;
using CrazyRisk.Core;

namespace CrazyRiskGame
{
    public class Juego : Microsoft.Xna.Framework.Game
    {
        private GraphicsDeviceManager graficos;
        private SpriteBatch spriteBatch = null!;

        // ====== Configuración general ======
        private const float MAP_SCALE = 2.35f; // un poco menor para ayudar a mostrar Oceanía
        private const float MAP_SHIFT_WHEN_SIDE = 0.28f; // porcentaje de side ancho que “descubre” Oceania

        private enum AppState { MenuPrincipal, MenuOpciones, MenuJugar, MenuPersonajes, EnJuego }
        private AppState estado = AppState.MenuPrincipal;

        // ====== UI/Assets generales ======
        private Texture2D? menuBg;
        private SpriteFont? font;
        private Texture2D pixel = null!;

        private struct Button { public Rectangle Bounds; public string Text; public bool Hover; }
        private readonly List<Button> botonesMenu = new();
        private readonly List<Button> botonesOpciones = new();
        private readonly List<Button> botonesJugar = new();
        private readonly List<Button> botonesPersonajes = new();

        // ====== Avatares (selección) ======
        private readonly List<Texture2D> avatarTex = new();
        private readonly List<Rectangle> avatarRects = new();
        private int selectedAvatarIndex = -1;
        private readonly Dictionary<int, int> avatarIndexByPlayer = new();

        // ====== Audio como SoundEffect ======
        private SoundEffect? musicMenuFx;
        private readonly List<SoundEffect?> musicGameFx = new();
        private SoundEffectInstance? currentMusic;
        private bool menuMusicStarted = false;

        // ====== Config ======
        private sealed class Config
        {
            public bool Fullscreen { get; set; } = false;
            public bool MusicEnabled { get; set; } = true;
            public float MusicVolume { get; set; } = 0.8f;
            public bool SfxEnabled { get; set; } = true;
        }
        private Config cfg = new();
        private string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");
        private readonly Random rng = new Random();

        // ====== Mapa / máscaras ======
        private Texture2D mapaVisible = null!;
        private readonly Dictionary<string, Texture2D> maskPorId = new();
        private readonly Dictionary<string, Color[]> maskPixelsPorId = new();
        private readonly Dictionary<string, int> idToIndex = new();
        private readonly List<string> indexToId = new();
        private int[,] ownerGrid = new int[1,1];
        private bool[,] adj = new bool[1,1];

        private string? territorioHover = null;
        private string? ultimoLogHover = null;
        private string? territorioSeleccionado = null;

        private KeyboardState prevKb;
        private MouseState prevMouse;
        private Map? mapaCore = null;

        private static readonly string[] IDS_TERRITORIOS = new[]
        {
            "NORTEAMERICA_CANADA","NORTEAMERICA_ESTADOS_UNIDOS","NORTEAMERICA_MEXICO","NORTEAMERICA_GROENLANDIA","NORTEAMERICA_CUBA","NORTEAMERICA_HAITI","NORTEAMERICA_GUATEMALA",
            "SURAMERICA_BRASIL","SURAMERICA_ARGENTINA","SURAMERICA_CHILE","SURAMERICA_PERU","SURAMERICA_COLOMBIA","SURAMERICA_URUGUAY",
            "EUROPA_ESPANA","EUROPA_FRANCIA","EUROPA_ALEMANIA","EUROPA_ITALIA","EUROPA_NORUEGA","EUROPA_GRECIA",
            "AFRICA_EGIPTO","AFRICA_NIGERIA","AFRICA_SUDAFRICA","AFRICA_MARRUECOS","AFRICA_KENIA","AFRICA_ETIOPIA","AFRICA_MADAGASCAR",
            "ASIA_CHINA","ASIA_INDIA","ASIA_JAPON","ASIA_COREA_DEL_SUR","ASIA_ARABIA_SAUDITA","ASIA_IRAN","ASIA_ISRAEL","ASIA_TURQUIA","ASIA_RUSIA","ASIA_INDONESIA","ASIA_TAILANDIA","ASIA_FILIPINAS",
            "OCEANIA_AUSTRALIA","OCEANIA_NUEVA_ZELANDA","OCEANIA_PAPUA_NUEVA_GUINEA","OCEANIA_FIYI"
        };

        // ====== Lógica base (dummy) ======
        private readonly List<Player> players = new()
        {
            new Player(0, "Azul", Color.CornflowerBlue),
            new Player(1, "Rojo", new Color(220,60,60))
        };
        private WorldState world = null!;

        // ====== UI EnJuego Provisional ======
        private enum SideTab { Refuerzos, Ataque, Movimiento, Cartas, Log }
        private SideTab sideTab = SideTab.Refuerzos;
        private bool sideCollapsed = false;

        private Rectangle hudTopRect;      // Barra superior
        private Rectangle sideRect;        // Panel lateral
        private Rectangle sideHeaderRect;  // Encabezado/tabs
        private Rectangle bottomRect;      // Barra inferior

        private readonly List<Button> sideTabButtons = new();
        private Button sideCollapseButton;
        private readonly List<Button> bottomButtons = new();

        // tamaños relativos (compactados)
        private const int HUD_TOP_H = 60;
        private const int BOTTOM_H  = 60;
        private int SIDE_W => sideCollapsed ? 24 : (int)(graficos.PreferredBackBufferWidth * 0.23f);
        private const int SIDE_HEADER_H = 34;
        private const int TAB_BUTTON_W = 92;

        // ====== DADOS ======
        private Texture2D[] diceFaces = Array.Empty<Texture2D>();
        private enum DiceState { Idle, Rolling, Show }
        private DiceState diceState = DiceState.Idle;
        private double diceTimer = 0;
        private const double DICE_ROLL_DURATION = 0.85; // segundos
        private readonly int[] diceResult = new int[3]; // 3 dados atacante (placeholder)
        private readonly int[] diceShown  = new int[3]; // lo que se muestra en animación
        private Rectangle lastBtnRollRect; // botón cacheado para coherencia

        // ====== Layout de ataque ======
        private struct AtaqueLayout
        {
            public Rectangle AttRect, DefRect, BtnRollRect, DiceStripRect, AutoCheckRect;
        }

        public Juego()
        {
            graficos = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
            Content.RootDirectory = DescubrirRootContenido();
        }

        private static string DescubrirRootContenido()
        {
            var baseDir = AppContext.BaseDirectory;
            string[] candidatos = new[]
            {
                Path.Combine("..","..","Content","bin","DesktopGL","Content"),
                Path.Combine("Content","bin","DesktopGL","Content"),
                "Content"
            };
            foreach (var rel in candidatos)
            {
                var abs = Path.GetFullPath(rel, baseDir);
                var esperado = Path.Combine(abs, "Sprites", "map_marcado.xnb");
                if (File.Exists(esperado))
                    return rel.Replace('\\','/');
            }
            return Path.Combine("..","..","Content","bin","DesktopGL","Content").Replace('\\','/');
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);

            // Texturas base
            mapaVisible = Content.Load<Texture2D>("Sprites/map_marcado");
            try { menuBg = Content.Load<Texture2D>("Sprites/UI/menu_bg"); } catch { menuBg = null; }
            try { font = Content.Load<SpriteFont>("Fonts/Default"); } catch { font = null; }

            pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });

            // Resolución basada en mapa
            graficos.PreferredBackBufferWidth  = (int)(mapaVisible.Width  * MAP_SCALE);
            graficos.PreferredBackBufferHeight = (int)(mapaVisible.Height * MAP_SCALE);
            graficos.ApplyChanges();

            // Máscaras por territorio
            for (int i = 0; i < IDS_TERRITORIOS.Length; i++)
            {
                var id = IDS_TERRITORIOS[i];
                idToIndex[id] = i;
                indexToId.Add(id);

                var tex = Content.Load<Texture2D>($"Sprites/masks/{id}");
                maskPorId[id] = tex;

                var data = new Color[tex.Width * tex.Height];
                tex.GetData(data);
                maskPixelsPorId[id] = data;
            }
            ConstruirPropiedadYAdyacencias();
            CargarMapaCoreSiExiste();

            // Música (SoundEffect)
            try { musicMenuFx = Content.Load<SoundEffect>("Audio/Music/music_menu"); } catch { musicMenuFx = null; }
            musicGameFx.Clear();
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game1")); } catch { musicGameFx.Add(null); }
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game2")); } catch { musicGameFx.Add(null); }
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game3")); } catch { musicGameFx.Add(null); }

            // Avatares
            avatarTex.Clear();
            for (int i = 1; i <= 6; i++)
            {
                try { avatarTex.Add(Content.Load<Texture2D>($"Sprites/avatars/perso{i}")); }
                catch { }
            }

            // DADOS - caras 1..6
            var faces = new List<Texture2D>();
            for (int i = 1; i <= 6; i++)
            {
                try { faces.Add(Content.Load<Texture2D>($"Sprites/Dice/Dice_{i}")); }
                catch { }
            }
            diceFaces = faces.ToArray();

            // Config
            CargarConfig();
            AplicarConfigVisual();
            if (!menuMusicStarted || currentMusic == null)
            {
                AplicarConfigAudio(estadoActualEsMenu: true);
                menuMusicStarted = true;
            }

            world = new WorldState(NeighborsOf);

            // Menús fuera de juego
            RebuildMenus();

            // Layout en juego (inicial)
            RebuildInGameLayout();
        }

        // ===================== Config =====================
        private void CargarConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath));
                    if (json != null) cfg = json;
                }
            }
            catch { cfg = new Config(); }
        }
        private void GuardarConfig()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
        private void AplicarConfigVisual()
        {
            graficos.IsFullScreen = cfg.Fullscreen;
            graficos.ApplyChanges();
        }

        private void StopMusic()
        {
            try { currentMusic?.Stop(); currentMusic?.Dispose(); }
            catch { }
            currentMusic = null;
        }
        private void PlayMusic(SoundEffect? fx)
        {
            StopMusic();
            if (fx == null) return;
            if (!cfg.MusicEnabled) return;
            try
            {
                SoundEffect.MasterVolume = MathHelper.Clamp(cfg.MusicVolume, 0f, 1f);
                currentMusic = fx.CreateInstance();
                currentMusic.IsLooped = true;
                currentMusic.Volume = MathHelper.Clamp(cfg.MusicVolume, 0f, 1f);
                currentMusic.Play();
            }
            catch { currentMusic = null; }
        }
        private void AplicarConfigAudio(bool estadoActualEsMenu)
        {
            if (!cfg.MusicEnabled) { StopMusic(); return; }
            if (estadoActualEsMenu)
            {
                if (currentMusic == null) PlayMusic(musicMenuFx);
            }
            else
            {
                var disponibles = new List<SoundEffect>();
                foreach (var s in musicGameFx) if (s != null) disponibles.Add(s!);
                if (disponibles.Count > 0) PlayMusic(disponibles[rng.Next(disponibles.Count)]);
                else StopMusic();
            }
        }
        private void CambiarEstado(AppState nuevo)
        {
            if (nuevo == AppState.EnJuego) AplicarConfigAudio(estadoActualEsMenu: false);
            else
            {
                if (!menuMusicStarted || currentMusic == null)
                {
                    AplicarConfigAudio(estadoActualEsMenu: true);
                    menuMusicStarted = true;
                }
            }

            estado = nuevo;

            if (estado == AppState.EnJuego)
            {
                world.Reset(indexToId);
                world.QuickDistribute(indexToId, players, baseTroopsPerTerritory: 3);
                territorioSeleccionado = null;

                // UI en juego
                sideTab = SideTab.Refuerzos;
                sideCollapsed = false;
                RebuildInGameLayout();

                // reiniciar dados
                diceState = DiceState.Idle;
                diceTimer = 0;
                for (int i = 0; i < diceShown.Length; i++) { diceShown[i] = 1; diceResult[i] = 1; }
            }
        }

        // ===================== Menús fuera de juego =====================
        private void RebuildMenus()
        {
            botonesMenu.Clear();
            botonesOpciones.Clear();
            botonesJugar.Clear();
            botonesPersonajes.Clear();
            avatarRects.Clear();

            int w = graficos.PreferredBackBufferWidth;
            int h = graficos.PreferredBackBufferHeight;

            int bw = (int)(w * 0.26f);
            int bh = 44;
            int cx = w / 2 - bw / 2;
            int gap = 10;

            // Principal
            int totalH = bh * 3 + gap * 2;
            int startY = h / 2 - totalH / 2;
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 0, bw, bh), Text = "Jugar" });
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 1, bw, bh), Text = "Opciones" });
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 2, bw, bh), Text = "Salir" });

            // Opciones
            int oy = h / 2 - (bh * 5 + gap * 4) / 2;
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 0, bw, bh), Text = $"Pantalla completa: {(cfg.Fullscreen ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 1, bw, bh), Text = $"Música: {(cfg.MusicEnabled ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 2, (bw - gap) / 2, bh), Text = "Vol -" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx + (bw + gap) / 2, oy + (bh + gap) * 2, (bw - gap) / 2, bh), Text = "Vol +" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 3, bw, bh), Text = $"SFX: {(cfg.SfxEnabled ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 4, bw, bh), Text = "Volver" });

            // Jugar
            int jy = h / 2 - (bh * 2 + gap) / 2;
            botonesJugar.Add(new Button { Bounds = new Rectangle(cx, jy + (bh + gap) * 0, bw, bh), Text = "Partida rapida" });
            botonesJugar.Add(new Button { Bounds = new Rectangle(cx, jy + (bh + gap) * 1, bw, bh), Text = "Volver" });

            // Selección de personajes (grid centrado 3x2)
            int cols = 3, rows = 2;
            int cellW = (int)(w * 0.15f);
            int cellH = (int)(h * 0.20f);
            int gapX = (int)(w * 0.032f);
            int gapY = (int)(h * 0.032f);
            int gridW = cols * cellW + (cols - 1) * gapX;
            int gridH = rows * cellH + (rows - 1) * gapY;
            int gx = (w - gridW) / 2;
            int gy = (int)(h * 0.2f);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= avatarTex.Count) break;
                    var rect = new Rectangle(gx + c * (cellW + gapX), gy + r * (cellH + gapY), cellW, cellH);
                    avatarRects.Add(rect);
                }
            }

            int bw2 = (int)(w * 0.24f);
            int baseY = gy + gridH + 26;
            botonesPersonajes.Add(new Button { Bounds = new Rectangle(w / 2 - bw2 / 2, baseY, bw2, 54), Text = "Confirmar" });
            botonesPersonajes.Add(new Button { Bounds = new Rectangle(w / 2 - bw2 / 2, baseY + 54 + 14, bw2, 54), Text = "Volver" });
        }

        // ===================== Layout EnJuego =====================
        private void RebuildInGameLayout()
        {
            int W = graficos.PreferredBackBufferWidth;
            int H = graficos.PreferredBackBufferHeight;

            hudTopRect = new Rectangle(0, 0, W, HUD_TOP_H);
            bottomRect = new Rectangle(0, H - BOTTOM_H, W, BOTTOM_H);

            int sideW = SIDE_W;
            sideRect = new Rectangle(W - sideW, HUD_TOP_H, sideW, H - HUD_TOP_H - BOTTOM_H);
            sideHeaderRect = new Rectangle(sideRect.X, sideRect.Y, sideRect.Width, SIDE_HEADER_H);

            // Tabs & botones panel
            sideTabButtons.Clear();
            if (!sideCollapsed)
            {
                string[] tabs = { "Refuerzos", "Ataque", "Movimiento", "Cartas", "Log" };
                for (int i = 0; i < tabs.Length; i++)
                {
                    int x = sideRect.X + 6 + i * (TAB_BUTTON_W + 6);
                    var r = new Rectangle(x, sideHeaderRect.Y + 4, TAB_BUTTON_W, sideHeaderRect.Height - 8);
                    sideTabButtons.Add(new Button { Bounds = r, Text = tabs[i], Hover = false });
                }
            }

            // Botón colapsar panel (a la izquierda del panel)
            int colBtnW = 24;
            sideCollapseButton = new Button
            {
                Bounds = new Rectangle(sideRect.X - colBtnW, sideRect.Y + 6, colBtnW, 26),
                Text = sideCollapsed ? ">" : "<",
                Hover = false
            };

            // Barra inferior: botones placeholder
            bottomButtons.Clear();
            int bw = 150, bh = 40, gap = 10;
            int startX = 10;
            string[] bb = { "Confirmar", "Deshacer", "Cancelar", "Terminar fase", "Menu" };
            for (int i = 0; i < bb.Length; i++)
            {
                var r = new Rectangle(startX + i * (bw + gap), bottomRect.Y + (bottomRect.Height - bh) / 2, bw, bh);
                bottomButtons.Add(new Button { Bounds = r, Text = bb[i], Hover = false });
            }
        }

        private AtaqueLayout GetAtaqueLayout()
        {
            var l = new AtaqueLayout();

            // área principal del panel (contenido)
            var content = new Rectangle(
                sideRect.X + 8,
                sideRect.Y + SIDE_HEADER_H + 8,
                sideRect.Width - 16,
                sideRect.Height - SIDE_HEADER_H - 16
            );

            // bloques compactos
            l.AttRect = new Rectangle(content.X, content.Y, content.Width, 56);
            l.DefRect = new Rectangle(content.X, l.AttRect.Bottom + 6, content.Width, 56);

            // check (placeholder)
            l.AutoCheckRect = new Rectangle(content.X, l.DefRect.Bottom + 8, 16, 16);

            // botón lanzar
            l.BtnRollRect = new Rectangle(content.X, l.DefRect.Bottom + 34, content.Width, 38);

            // zona dados
            l.DiceStripRect = new Rectangle(content.X, l.BtnRollRect.Bottom + 6, content.Width, 60);

            return l;
        }

        // ===================== Mapa / Adyacencia =====================
        private static bool EsGris(Color c)
        {
            const int DELTA_GRAY = 12;
            const int MIN_LUMA = 24;
            const int MAX_LUMA = 240;
            int maxC = Math.Max(c.R, Math.Max(c.G, c.B));
            int minC = Math.Min(c.R, Math.Min(c.G, c.B));
            int luma = (c.R + c.G + c.B) / 3;
            return (maxC - minC) <= DELTA_GRAY && luma >= MIN_LUMA && luma <= MAX_LUMA;
        }
        private void ConstruirPropiedadYAdyacencias()
        {
            int w = mapaVisible.Width;
            int h = mapaVisible.Height;

            ownerGrid = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    ownerGrid[x, y] = -1;

            for (int ti = 0; ti < IDS_TERRITORIOS.Length; ti++)
            {
                string id = IDS_TERRITORIOS[ti];
                var tex = maskPorId[id];
                var buf = maskPixelsPorId[id];

                int tw = tex.Width, th = tex.Height;
                for (int y = 0; y < th; y++)
                {
                    int row = y * tw;
                    for (int x = 0; x < tw; x++)
                    {
                        var c = buf[row + x];
                        if (EsGris(c)) ownerGrid[x, y] = ti;
                    }
                }
            }

            int n = IDS_TERRITORIOS.Length;
            adj = new bool[n, n];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int a = ownerGrid[x, y];
                    if (a < 0) continue;

                    if (x + 1 < w)
                    {
                        int b = ownerGrid[x + 1, y];
                        if (b >= 0 && b != a) { adj[a, b] = adj[b, a] = true; }
                    }
                    if (y + 1 < h)
                    {
                        int b = ownerGrid[x, y + 1];
                        if (b >= 0 && b != a) { adj[a, b] = adj[b, a] = true; }
                    }
                }
            }
        }
        private IEnumerable<string> NeighborsOf(string id)
        {
            int a = idToIndex[id];
            for (int b = 0; b < indexToId.Count; b++)
                if (adj[a, b]) yield return indexToId[b];
        }
        private void CargarMapaCoreSiExiste()
        {
            try
            {
                var jsonPath = Path.Combine("Content", "Data", "territories.json");
                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine("[CORE] No se encontro Content/Data/territories.json (presiona J para exportar).");
                    return;
                }
                var json = File.ReadAllText(jsonPath);
                var mapa = MapLoader.FromJson(json);
                mapaCore = mapa;
                Console.WriteLine("[CORE] Mapa cargado: " + mapa.Territories.Length + " territorios.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CORE] Error cargando territories.json: " + ex.Message);
            }
        }

        // ===================== Update =====================
        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            var mouse = Mouse.GetState();
            var pos = new Point(mouse.X, mouse.Y);

            bool clickLeft = prevMouse.LeftButton == ButtonState.Released && mouse.LeftButton == ButtonState.Pressed;

            if (kb.IsKeyDown(Keys.Escape))
            {
                switch (estado)
                {
                    case AppState.MenuOpciones:
                    case AppState.MenuJugar:
                    case AppState.MenuPersonajes:
                        CambiarEstado(AppState.MenuPrincipal); break;
                    case AppState.EnJuego:
                        CambiarEstado(AppState.MenuPrincipal); break;
                    case AppState.MenuPrincipal:
                        Exit(); break;
                }
            }

            // ===== Menús fuera de juego =====
            if (estado != AppState.EnJuego)
            {
                if (estado == AppState.MenuOpciones || estado == AppState.MenuJugar || estado == AppState.MenuPrincipal)
                {
                    var lista = estado switch
                    {
                        AppState.MenuOpciones => botonesOpciones,
                        AppState.MenuJugar => botonesJugar,
                        _ => botonesMenu
                    };

                    for (int i = 0; i < lista.Count; i++)
                    {
                        var b = lista[i];
                        b.Hover = b.Bounds.Contains(pos);
                        if (estado == AppState.MenuOpciones) botonesOpciones[i] = b;
                        else if (estado == AppState.MenuJugar) botonesJugar[i] = b;
                        else botonesMenu[i] = b;
                    }

                    if (clickLeft)
                    {
                        if (estado == AppState.MenuPrincipal)
                        {
                            if (botonesMenu[0].Bounds.Contains(pos)) { CambiarEstado(AppState.MenuJugar); }
                            else if (botonesMenu[1].Bounds.Contains(pos)) { CambiarEstado(AppState.MenuOpciones); }
                            else if (botonesMenu[2].Bounds.Contains(pos)) { Exit(); }
                        }
                        else if (estado == AppState.MenuOpciones)
                        {
                            if (botonesOpciones[0].Bounds.Contains(pos))
                            { cfg.Fullscreen = !cfg.Fullscreen; AplicarConfigVisual(); RebuildMenus(); GuardarConfig(); }
                            else if (botonesOpciones[1].Bounds.Contains(pos))
                            { cfg.MusicEnabled = !cfg.MusicEnabled; if (!cfg.MusicEnabled) StopMusic(); else if (estado != AppState.EnJuego) AplicarConfigAudio(true); RebuildMenus(); GuardarConfig(); }
                            else if (botonesOpciones[2].Bounds.Contains(pos))
                            { cfg.MusicVolume = MathF.Max(0f, cfg.MusicVolume - 0.1f); if (currentMusic != null) currentMusic.Volume = cfg.MusicVolume; SoundEffect.MasterVolume = cfg.MusicVolume; GuardarConfig(); }
                            else if (botonesOpciones[3].Bounds.Contains(pos))
                            { cfg.MusicVolume = MathF.Min(1f, cfg.MusicVolume + 0.1f); if (currentMusic != null) currentMusic.Volume = cfg.MusicVolume; SoundEffect.MasterVolume = cfg.MusicVolume; GuardarConfig(); }
                            else if (botonesOpciones[4].Bounds.Contains(pos))
                            { cfg.SfxEnabled = !cfg.SfxEnabled; RebuildMenus(); GuardarConfig(); }
                            else if (botonesOpciones[5].Bounds.Contains(pos))
                            { CambiarEstado(AppState.MenuPrincipal); }

                            // refrescar textos
                            var b0 = botonesOpciones[0]; b0.Text = $"Pantalla completa: {(cfg.Fullscreen ? "ON" : "OFF")}"; botonesOpciones[0] = b0;
                            var b1 = botonesOpciones[1]; b1.Text = $"Musica: {(cfg.MusicEnabled ? "ON" : "OFF")}"; botonesOpciones[1] = b1;
                            var b4 = botonesOpciones[4]; b4.Text = $"SFX: {(cfg.SfxEnabled ? "ON" : "OFF")}"; botonesOpciones[4] = b4;
                        }
                        else if (estado == AppState.MenuJugar)
                        {
                            if (botonesJugar[0].Bounds.Contains(pos))
                            {
                                selectedAvatarIndex = -1;
                                CambiarEstado(AppState.MenuPersonajes);
                            }
                            else if (botonesJugar[1].Bounds.Contains(pos))
                            {
                                CambiarEstado(AppState.MenuPrincipal);
                            }
                        }
                    }

                    prevKb = kb;
                    prevMouse = mouse;
                    base.Update(gameTime);
                    return;
                }

                // Selección de Personajes
                if (estado == AppState.MenuPersonajes)
                {
                    for (int i = 0; i < botonesPersonajes.Count; i++)
                    {
                        var b = botonesPersonajes[i];
                        b.Hover = b.Bounds.Contains(pos);
                        botonesPersonajes[i] = b;
                    }

                    if (clickLeft)
                    {
                        for (int i = 0; i < avatarRects.Count && i < avatarTex.Count; i++)
                            if (avatarRects[i].Contains(pos)) { selectedAvatarIndex = i; break; }

                        if (botonesPersonajes[0].Bounds.Contains(pos))
                        {
                            if (selectedAvatarIndex >= 0)
                            {
                                avatarIndexByPlayer[0] = selectedAvatarIndex;
                                CambiarEstado(AppState.EnJuego);
                            }
                            else Console.WriteLine("[UI] Debes elegir un avatar.");
                        }
                        else if (botonesPersonajes[1].Bounds.Contains(pos))
                        {
                            CambiarEstado(AppState.MenuJugar);
                        }
                    }

                    prevKb = kb;
                    prevMouse = mouse;
                    base.Update(gameTime);
                    return;
                }
            }

            // ===== En Juego =====
            var mouseState = mouse;
            int mx = (int)((mouseState.X - MapDrawOffsetX()) / MAP_SCALE);
            int my = (int)((mouseState.Y - MapDrawOffsetY()) / MAP_SCALE);

            // Si el click cae sobre UI lateral o barras, no interactuamos con mapa
            bool sobreUI = hudTopRect.Contains(new Point(mouseState.X, mouseState.Y))
                           || bottomRect.Contains(new Point(mouseState.X, mouseState.Y))
                           || sideRect.Contains(new Point(mouseState.X, mouseState.Y))
                           || sideCollapseButton.Bounds.Contains(new Point(mouseState.X, mouseState.Y));

            if (!sobreUI)
            {
                territorioHover = DetectarTerritorioPorGris(mx, my);
                if (territorioHover != ultimoLogHover)
                {
                    Console.WriteLine(territorioHover ?? "SinTerritorio");
                    ultimoLogHover = territorioHover;
                }

                if (clickLeft)
                {
                    if (territorioSeleccionado == null)
                    {
                        territorioSeleccionado = territorioHover;
                    }
                    else if (territorioHover != null && territorioHover != territorioSeleccionado)
                    {
                        // Placeholder: futura lógica
                        territorioSeleccionado = territorioHover;
                    }
                }

                if (mouseState.RightButton == ButtonState.Pressed || (kb.IsKeyDown(Keys.Back) && prevKb.IsKeyUp(Keys.Back)))
                    territorioSeleccionado = null;
            }

            // Atajo export JSON
            if (kb.IsKeyDown(Keys.J) && !prevKb.IsKeyDown(Keys.J))
            {
                ExportarTerritoriesJson();
                CargarMapaCoreSiExiste();
            }

            // ----- UI lateral: hover & clicks -----
            var scb = sideCollapseButton;
            scb.Hover = sideCollapseButton.Bounds.Contains(new Point(mouseState.X, mouseState.Y));
            sideCollapseButton = scb;
            if (clickLeft && sideCollapseButton.Bounds.Contains(new Point(mouseState.X, mouseState.Y)))
            {
                sideCollapsed = !sideCollapsed;
                sideCollapseButton.Text = sideCollapsed ? ">" : "<";
                RebuildInGameLayout();
            }

            if (!sideCollapsed)
            {
                for (int i = 0; i < sideTabButtons.Count; i++)
                {
                    var b = sideTabButtons[i];
                    b.Hover = b.Bounds.Contains(new Point(mouseState.X, mouseState.Y));
                    sideTabButtons[i] = b;

                    if (clickLeft && b.Bounds.Contains(new Point(mouseState.X, mouseState.Y)))
                    {
                        sideTab = (SideTab)i;
                    }
                }
            }

            for (int i = 0; i < bottomButtons.Count; i++)
            {
                var b = bottomButtons[i];
                b.Hover = b.Bounds.Contains(new Point(mouseState.X, mouseState.Y));
                bottomButtons[i] = b;

                if (clickLeft && b.Bounds.Contains(new Point(mouseState.X, mouseState.Y)))
                {
                    Console.WriteLine($"[UI] Click '{b.Text}' (sin acción)");
                }
            }

            // --- Click en "Lanzar dados" cuando la pestaña es Ataque ---
            if (estado == AppState.EnJuego && sideTab == SideTab.Ataque)
            {
                var L = GetAtaqueLayout();
                lastBtnRollRect = L.BtnRollRect;

                if (clickLeft && L.BtnRollRect.Contains(new Point(mouse.X, mouse.Y)))
                {
                    if (diceFaces.Length >= 6)
                    {
                        diceState = DiceState.Rolling;
                        diceTimer = 0;
                        for (int i2 = 0; i2 < diceResult.Length; i2++)
                        {
                            diceResult[i2] = rng.Next(1, 7);
                            diceShown[i2]  = rng.Next(1, 7);
                        }
                    }
                }
            }

            // Avance de animación de dados
            if (diceState == DiceState.Rolling)
            {
                diceTimer += gameTime.ElapsedGameTime.TotalSeconds;

                if (diceTimer < DICE_ROLL_DURATION)
                {
                    // Cambios rápidos de caras (cada ~70ms)
                    if (diceTimer - _lastDiceFlipTime >= 0.07)
                    {
                        _lastDiceFlipTime = diceTimer;
                        for (int i = 0; i < diceShown.Length; i++)
                            diceShown[i] = rng.Next(1, 7);
                    }
                }
                else
                {
                    for (int i = 0; i < diceShown.Length; i++)
                        diceShown[i] = diceResult[i];
                    diceState = DiceState.Show;
                }
            }

            prevKb = kb;
            prevMouse = mouse;
            base.Update(gameTime);
        }
        private double _lastDiceFlipTime = 0;

        private float MapDrawOffsetX()
        {
            // corre el mapa un poco a la izquierda cuando el panel está abierto para ganar espacio en el este (Oceanía)
            if (!sideCollapsed)
            {
                float maxShift = (SIDE_W * MAP_SHIFT_WHEN_SIDE);
                return -maxShift;
            }
            return 0f;
        }
        private float MapDrawOffsetY() => 0f;

        private string? DetectarTerritorioPorGris(int x, int y)
        {
            if (x < 0 || y < 0 || x >= mapaVisible.Width || y >= mapaVisible.Height)
                return null;

            foreach (var id in IDS_TERRITORIOS)
            {
                var tex = maskPorId[id];
                if (x >= tex.Width || y >= tex.Height) continue;

                var buf = maskPixelsPorId[id];
                var c = buf[y * tex.Width + x];
                if (EsGris(c)) return id;
            }
            return null;
        }

        private sealed class TerritoryDTO { public string id { get; set; } = ""; public string[] neighbors { get; set; } = Array.Empty<string>(); }
        private sealed class TerritoriesRootDTO { public TerritoryDTO[] territories { get; set; } = Array.Empty<TerritoryDTO>(); }

        private void ExportarTerritoriesJson()
        {
            var list = new List<TerritoryDTO>();
            for (int a = 0; a < indexToId.Count; a++)
            {
                var vecinos = new List<string>();
                for (int b = 0; b < indexToId.Count; b++)
                    if (adj[a, b]) vecinos.Add(indexToId[b]);

                list.Add(new TerritoryDTO { id = indexToId[a], neighbors = vecinos.ToArray() });
            }

            var root = new TerritoriesRootDTO { territories = list.ToArray() };

            string outDir = Path.Combine("Content", "Data");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "territories.json");

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outPath, json);
            Console.WriteLine("[OK] Exportado: " + outPath);
        }

        // ===================== Draw =====================
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Menús fuera de juego
            if (estado != AppState.EnJuego)
            {
                var bg = menuBg ?? mapaVisible;
                var scale = ComputeScaleToFit(bg.Width, bg.Height, graficos.PreferredBackBufferWidth, graficos.PreferredBackBufferHeight);
                var pos = new Vector2(
                    (graficos.PreferredBackBufferWidth - bg.Width * scale) * 0.5f,
                    (graficos.PreferredBackBufferHeight - bg.Height * scale) * 0.5f
                );
                spriteBatch.Draw(bg, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

                var lista = estado switch
                {
                    AppState.MenuOpciones => botonesOpciones,
                    AppState.MenuJugar => botonesJugar,
                    AppState.MenuPersonajes => botonesPersonajes,
                    _ => botonesMenu
                };

                // Pantalla de selección de personajes dibuja distinto
                if (estado == AppState.MenuPersonajes)
                {
                    if (font != null)
                    {
                        string titulo = "Selecciona tu personaje";
                        var size = font.MeasureString(titulo);
                        spriteBatch.DrawString(font, titulo, new Vector2(
                            (graficos.PreferredBackBufferWidth - size.X) * 0.5f,
                            pos.Y + 12), Color.White);
                    }

                    for (int i = 0; i < avatarRects.Count && i < avatarTex.Count; i++)
                    {
                        var rect = avatarRects[i];
                        var tex = avatarTex[i];
                        spriteBatch.Draw(pixel, rect, new Color(0, 0, 0, 120));
                        DrawRect(rect, Color.White, 2);

                        float sx = (float)rect.Width / tex.Width;
                        float sy = (float)rect.Height / tex.Height;
                        float sc = MathF.Min(sx, sy) * 0.92f;
                        var drawSize = new Vector2(tex.Width * sc, tex.Height * sc);
                        var posAv = new Vector2(rect.X + (rect.Width - drawSize.X) * 0.5f, rect.Y + (rect.Height - drawSize.Y) * 0.5f);
                        spriteBatch.Draw(tex, posAv, null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);

                        if (i == selectedAvatarIndex)
                            DrawRect(new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6), new Color(0, 255, 0, 220), 3);
                    }

                    for (int i = 0; i < botonesPersonajes.Count; i++)
                    {
                        var b = botonesPersonajes[i];
                        var bgc = b.Hover ? new Color(255,255,255,190) : new Color(255,255,255,140);
                        spriteBatch.Draw(pixel, new Rectangle(b.Bounds.X + 3, b.Bounds.Y + 3, b.Bounds.Width, b.Bounds.Height), new Color(0, 0, 0, 80));
                        spriteBatch.Draw(pixel, b.Bounds, bgc);
                        DrawRect(b.Bounds, new Color(0, 0, 0, 180), 2);

                        if (font != null)
                        {
                            var size = font.MeasureString(b.Text);
                            var tx = b.Bounds.X + (b.Bounds.Width - size.X) * 0.5f;
                            var ty = b.Bounds.Y + (b.Bounds.Height - size.Y) * 0.5f;
                            spriteBatch.DrawString(font, b.Text, new Vector2(tx, ty), Color.Black);
                        }
                    }

                    spriteBatch.End();
                    base.Draw(gameTime);
                    return;
                }

                // Botones Menú / Opciones / Jugar
                for (int i = 0; i < lista.Count; i++)
                {
                    var b = lista[i];
                    var bgc = b.Hover ? new Color(255,255,255,190) : new Color(255,255,255,140);
                    spriteBatch.Draw(pixel, new Rectangle(b.Bounds.X + 3, b.Bounds.Y + 3, b.Bounds.Width, b.Bounds.Height), new Color(0, 0, 0, 80));
                    spriteBatch.Draw(pixel, b.Bounds, bgc);
                    DrawRect(b.Bounds, new Color(0, 0, 0, 180), 2);

                    if (font != null)
                    {
                        var size = font.MeasureString(b.Text);
                        var tx = b.Bounds.X + (b.Bounds.Width - size.X) * 0.5f;
                        var ty = b.Bounds.Y + (b.Bounds.Height - size.Y) * 0.5f;
                        spriteBatch.DrawString(font, b.Text, new Vector2(tx, ty), Color.Black);
                    }
                }

                spriteBatch.End();
                base.Draw(gameTime);
                return;
            }

            // ===== Dibujo En Juego =====
            // 1) Mapa (con offset para descubrir Oceanía cuando panel abierto)
            var mapOffset = new Vector2(MapDrawOffsetX(), MapDrawOffsetY());
            spriteBatch.Draw(mapaVisible, mapOffset, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);

            if (territorioSeleccionado != null && maskPorId.TryGetValue(territorioSeleccionado, out var selMask))
                spriteBatch.Draw(selMask, mapOffset, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);
            else if (territorioHover != null && maskPorId.TryGetValue(territorioHover, out var hoverMask))
                spriteBatch.Draw(hoverMask, mapOffset, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);

            // 2) HUD superior
            spriteBatch.Draw(pixel, hudTopRect, new Color(0, 0, 0, 150));
            DrawRect(hudTopRect, new Color(255, 255, 255, 160), 2);

            if (font != null)
            {
                int active = 0;
                string fase = sideTab switch
                {
                    SideTab.Refuerzos => "Fase: Refuerzos",
                    SideTab.Ataque => "Fase: Ataque",
                    SideTab.Movimiento => "Fase: Movimiento",
                    _ => "Fase: Gestion"
                };

                // Avatar si existe
                if (avatarIndexByPlayer.TryGetValue(0, out int avIdx) && avIdx >= 0 && avIdx < avatarTex.Count)
                {
                    var av = avatarTex[avIdx];
                    int avSize = hudTopRect.Height - 10;
                    float sc = MathF.Min((float)avSize / av.Width, (float)avSize / av.Height);
                    var posAv = new Vector2(hudTopRect.X + 8, hudTopRect.Y + 5);
                    spriteBatch.Draw(av, posAv, null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);

                    spriteBatch.DrawString(font, $"Jugador: {players[active].Name}", new Vector2(posAv.X + avSize + 8, hudTopRect.Y + 6), Color.White);
                    spriteBatch.DrawString(font, fase, new Vector2(posAv.X + avSize + 8, hudTopRect.Y + 30), Color.White);
                }
                else
                {
                    spriteBatch.DrawString(font, $"Jugador: {players[active].Name}", new Vector2(hudTopRect.X + 8, hudTopRect.Y + 6), Color.White);
                    spriteBatch.DrawString(font, fase, new Vector2(hudTopRect.X + 8, hudTopRect.Y + 30), Color.White);
                }

                // Acciones/ayuda breve al centro
                string hint = sideTab switch
                {
                    SideTab.Refuerzos => "Coloca refuerzos en tus territorios",
                    SideTab.Ataque => "Selecciona atacante y defensor adyacentes",
                    SideTab.Movimiento => "Mueve tropas entre territorios conectados",
                    SideTab.Cartas => "Gestiona sets y bonus",
                    SideTab.Log => "Consulta historial de jugadas",
                    _ => ""
                };
                var size = font.MeasureString(hint);
                spriteBatch.DrawString(font, hint, new Vector2((graficos.PreferredBackBufferWidth - size.X) * 0.5f, hudTopRect.Y + (hudTopRect.Height - size.Y) * 0.5f), Color.White);

                // Botón ayuda (placeholder)
                string help = "[?] Ayuda";
                var sh = font.MeasureString(help);
                spriteBatch.DrawString(font, help, new Vector2(graficos.PreferredBackBufferWidth - sh.X - 10, hudTopRect.Y + 6), Color.White);
            }

            // 3) Panel lateral derecho (colapsable)
            // Botón colapsar (pestaña)
            var tabCol = sideCollapseButton.Hover ? new Color(255,255,255,220) : new Color(255,255,255,180);
            spriteBatch.Draw(pixel, sideCollapseButton.Bounds, tabCol);
            DrawRect(sideCollapseButton.Bounds, new Color(0,0,0,200), 2);
            if (font != null)
            {
                var ts = font.MeasureString(sideCollapseButton.Text);
                spriteBatch.DrawString(font,
                    sideCollapseButton.Text,
                    new Vector2(sideCollapseButton.Bounds.X + (sideCollapseButton.Bounds.Width - ts.X) * 0.5f,
                                sideCollapseButton.Bounds.Y + (sideCollapseButton.Bounds.Height - ts.Y) * 0.5f),
                    Color.Black);
            }

            // Cuerpo panel si está expandido
            if (!sideCollapsed)
            {
                // cuerpo
                spriteBatch.Draw(pixel, sideRect, new Color(0, 0, 0, 150));
                DrawRect(sideRect, new Color(255, 255, 255, 160), 2);

                // header
                spriteBatch.Draw(pixel, sideHeaderRect, new Color(255, 255, 255, 30));
                DrawRect(sideHeaderRect, new Color(255, 255, 255, 90), 1);

                // tabs
                for (int i = 0; i < sideTabButtons.Count; i++)
                {
                    var b = sideTabButtons[i];
                    var active = (i == (int)sideTab);
                    var bgc = active ? new Color(255,255,255,220) : (b.Hover ? new Color(255,255,255,180) : new Color(255,255,255,120));
                    var fg  = Color.Black;

                    spriteBatch.Draw(pixel, b.Bounds, bgc);
                    DrawRect(b.Bounds, new Color(0,0,0,200), active ? 2 : 1);

                    if (font != null)
                    {
                        var s = font.MeasureString(b.Text);
                        spriteBatch.DrawString(font, b.Text, new Vector2(b.Bounds.X + (b.Bounds.Width - s.X) * 0.5f, b.Bounds.Y + (b.Bounds.Height - s.Y) * 0.5f), fg);
                    }
                }

                // contenido de pestaña
                var content = new Rectangle(sideRect.X + 8, sideRect.Y + SIDE_HEADER_H + 8, sideRect.Width - 16, sideRect.Height - SIDE_HEADER_H - 16);
                DrawSideTabContent(content);
            }

            // 4) Barra inferior
            spriteBatch.Draw(pixel, bottomRect, new Color(0, 0, 0, 150));
            DrawRect(bottomRect, new Color(255, 255, 255, 160), 2);

            foreach (var b in bottomButtons)
            {
                var bgc = b.Hover ? new Color(255,255,255,210) : new Color(255,255,255,160);
                spriteBatch.Draw(pixel, new Rectangle(b.Bounds.X + 3, b.Bounds.Y + 3, b.Bounds.Width, b.Bounds.Height), new Color(0,0,0,70));
                spriteBatch.Draw(pixel, b.Bounds, bgc);
                DrawRect(b.Bounds, new Color(0,0,0,190), 2);

                if (font != null)
                {
                    var s = font.MeasureString(b.Text);
                    spriteBatch.DrawString(font, b.Text, new Vector2(b.Bounds.X + (b.Bounds.Width - s.X) * 0.5f, b.Bounds.Y + (b.Bounds.Height - s.Y) * 0.5f), Color.Black);
                }
            }

            // 5) HUD info de selección al vuelo (pequeño overlay)
            if (font != null)
            {
                string sel = territorioSeleccionado ?? "(ninguno)";
                string hov = territorioHover ?? "(ninguno)";
                var measure = Math.Max(font.MeasureString(sel).X, font.MeasureString(hov).X);
                var box = new Rectangle(10, bottomRect.Y - 52, (int)Math.Max(240, measure + 24), 44);
                spriteBatch.Draw(pixel, box, new Color(0,0,0,120));
                DrawRect(box, new Color(255,255,255,140), 2);
                spriteBatch.DrawString(font, $"Sel: {sel}", new Vector2(box.X + 8, box.Y + 6), Color.White);
                spriteBatch.DrawString(font, $"Hover: {hov}", new Vector2(box.X + 8, box.Y + 24), Color.White);
            }

            spriteBatch.End();
            base.Draw(gameTime);
        }

        // ---- Contenido provisional de pestañas ----
        private void DrawSideTabContent(Rectangle area)
        {
            DrawRect(area, new Color(255,255,255,80), 1);
            if (font == null) return;

            switch (sideTab)
            {
                case SideTab.Refuerzos:
                {
                    spriteBatch.DrawString(font, "REFUERZOS", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var list = new Rectangle(area.X + 6, area.Y + 26, area.Width - 12, 140);
                    spriteBatch.Draw(pixel, list, new Color(255,255,255,20));
                    DrawRect(list, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "- Territorio A (Propio)", new Vector2(list.X + 8, list.Y + 6), Color.White);
                    spriteBatch.DrawString(font, "- Territorio B (Propio)", new Vector2(list.X + 8, list.Y + 26), Color.White);
                    spriteBatch.DrawString(font, "- ...", new Vector2(list.X + 8, list.Y + 46), Color.White);

                    var row = new Rectangle(area.X + 6, list.Bottom + 6, area.Width - 12, 34);
                    spriteBatch.Draw(pixel, row, new Color(255,255,255,20));
                    DrawRect(row, new Color(255,255,255,60), 1);
                    DrawUITextButton(new Rectangle(row.X + 6, row.Y + 3, 52, 28), "+1");
                    DrawUITextButton(new Rectangle(row.X + 6 + 60, row.Y + 3, 52, 28), "+5");
                    DrawUITextButton(new Rectangle(row.X + 6 + 120, row.Y + 3, 52, 28), "-1");
                    DrawUITextButton(new Rectangle(row.X + 6 + 180, row.Y + 3, 52, 28), "-5");

                    DrawUIPrimaryButton(new Rectangle(area.X + 6, row.Bottom + 6, area.Width - 12, 38), "Colocar en territorio seleccionado");
                    spriteBatch.DrawString(font, "Tropas disponibles: 0 (placeholder)", new Vector2(area.X + 6, row.Bottom + 48), Color.White);
                    break;
                }
                case SideTab.Ataque:
                {
                    var L = GetAtaqueLayout();

                    // ATT
                    spriteBatch.Draw(pixel, L.AttRect, new Color(255,255,255,20));
                    DrawRect(L.AttRect, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "Atacante: (selecciona en el mapa)", new Vector2(L.AttRect.X + 6, L.AttRect.Y + 4), Color.White);
                    spriteBatch.DrawString(font, "Tropas: -- | Dueno: --", new Vector2(L.AttRect.X + 6, L.AttRect.Y + 26), Color.White);

                    // DEF
                    spriteBatch.Draw(pixel, L.DefRect, new Color(255,255,255,20));
                    DrawRect(L.DefRect, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "Defensor: (elige adyacente enemigo)", new Vector2(L.DefRect.X + 6, L.DefRect.Y + 4), Color.White);
                    spriteBatch.DrawString(font, "Tropas: -- | Dueno: --", new Vector2(L.DefRect.X + 6, L.DefRect.Y + 26), Color.White);

                    // check auto (placeholder)
                    DrawUICheck(L.AutoCheckRect, false, "Ataque automatico");

                    // botón lanzar
                    DrawUIPrimaryButton(L.BtnRollRect, "Lanzar dados");

                    // tira de dados
                    DrawRect(L.DiceStripRect, new Color(255,255,255,60), 1);
                    if (diceFaces.Length >= 6)
                    {
                        int n = diceShown.Length;
                        int gap = 10;
                        int dieSize = Math.Min( (L.DiceStripRect.Height - 8), (L.DiceStripRect.Width - gap*(n+1)) / n );
                        dieSize = Math.Max(24, dieSize);
                        for (int i = 0; i < n; i++)
                        {
                            int val = Math.Clamp(diceShown[i], 1, 6);
                            var tex = diceFaces[val - 1];
                            int x = L.DiceStripRect.X + gap + i * (dieSize + gap);
                            int y = L.DiceStripRect.Y + (L.DiceStripRect.Height - dieSize) / 2;
                            float sc = MathF.Min((float)dieSize / tex.Width, (float)dieSize / tex.Height);
                            spriteBatch.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                        }
                    }
                    break;
                }
                case SideTab.Movimiento:
                {
                    spriteBatch.DrawString(font, "MOVIMIENTO", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var sel = new Rectangle(area.X + 6, area.Y + 26, area.Width - 12, 52);
                    spriteBatch.Draw(pixel, sel, new Color(255,255,255,20));
                    DrawRect(sel, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "Origen/Destino: seleccionar en el mapa", new Vector2(sel.X + 6, sel.Y + 6), Color.White);

                    // Slider falso
                    spriteBatch.DrawString(font, "Cantidad a mover:", new Vector2(area.X + 6, sel.Bottom + 8), Color.White);
                    var track = new Rectangle(area.X + 6, sel.Bottom + 26, area.Width - 12, 4);
                    spriteBatch.Draw(pixel, track, new Color(255,255,255,60));
                    var handle = new Rectangle(area.X + track.Width / 2 + 6, track.Y - 5, 10, 16);
                    spriteBatch.Draw(pixel, handle, new Color(255,255,255,200));
                    DrawUIPrimaryButton(new Rectangle(area.X + 6, track.Bottom + 10, area.Width - 12, 38), "Mover tropas");
                    break;
                }
                case SideTab.Cartas:
                {
                    spriteBatch.DrawString(font, "CARTAS", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var grid = new Rectangle(area.X + 6, area.Y + 26, area.Width - 12, area.Height - 80);
                    spriteBatch.Draw(pixel, grid, new Color(255,255,255,20));
                    DrawRect(grid, new Color(255,255,255,60), 1);

                    int cols = 3, rows = 2, gap = 8;
                    int cw = (grid.Width - gap*(cols+1)) / cols;
                    int ch = (grid.Height - gap*(rows+1)) / rows;
                    for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        var rct = new Rectangle(grid.X + gap + c*(cw+gap), grid.Y + gap + r*(ch+gap), cw, ch);
                        spriteBatch.Draw(pixel, rct, new Color(255,255,255,30));
                        DrawRect(rct, new Color(255,255,255,90), 1);
                    }
                    DrawUIPrimaryButton(new Rectangle(area.X + 6, area.Bottom - 46, area.Width - 12, 38), "Canjear set");
                    break;
                }
                case SideTab.Log:
                {
                    spriteBatch.DrawString(font, "LOG / HISTORIAL", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var box = new Rectangle(area.X + 6, area.Y + 26, area.Width - 12, area.Height - 32);
                    spriteBatch.Draw(pixel, box, new Color(255,255,255,20));
                    DrawRect(box, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "- Jugador Azul coloco 3 en NORTEAMERICA_CANADA", new Vector2(box.X + 6, box.Y + 6), Color.White);
                    spriteBatch.DrawString(font, "- Ataque: ESPANA -> FRANCIA (3v2). Tiradas: 6-5 vs 5-2", new Vector2(box.X + 6, box.Y + 24), Color.White);
                    spriteBatch.DrawString(font, "- Conquista: FRANCIA", new Vector2(box.X + 6, box.Y + 42), Color.White);
                    break;
                }
            }
        }

        // ===================== Utilidades Draw/UI =====================
        private float ComputeScaleToFit(int texW, int texH, int viewW, int viewH)
        {
            float sx = (float)viewW / texW;
            float sy = (float)viewH / texH;
            return MathF.Min(sx, sy);
        }
        private void DrawRect(Rectangle r, Color c, int thickness)
        {
            spriteBatch.Draw(pixel, new Rectangle(r.Left, r.Top, r.Width, thickness), c);
            spriteBatch.Draw(pixel, new Rectangle(r.Left, r.Bottom - thickness, r.Width, thickness), c);
            spriteBatch.Draw(pixel, new Rectangle(r.Left, r.Top, thickness, r.Height), c);
            spriteBatch.Draw(pixel, new Rectangle(r.Right - thickness, r.Top, thickness, r.Height), c);
        }
        private void DrawUIPrimaryButton(Rectangle r, string text)
        {
            spriteBatch.Draw(pixel, new Rectangle(r.X + 2, r.Y + 2, r.Width, r.Height), new Color(0,0,0,70));
            spriteBatch.Draw(pixel, r, new Color(255,255,255,210));
            DrawRect(r, new Color(0,0,0,190), 2);
            if (font != null)
            {
                var s = font.MeasureString(text);
                spriteBatch.DrawString(font, text, new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f), Color.Black);
            }
        }
        private void DrawUITextButton(Rectangle r, string text)
        {
            spriteBatch.Draw(pixel, r, new Color(255,255,255,130));
            DrawRect(r, new Color(0,0,0,180), 1);
            if (font != null)
            {
                var s = font.MeasureString(text);
                spriteBatch.DrawString(font, text, new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f), Color.Black);
            }
        }
        private void DrawUICheck(Rectangle box, bool on, string label)
        {
            spriteBatch.Draw(pixel, box, Color.White);
            DrawRect(box, new Color(0,0,0,200), 1);
            if (on)
            {
                var inner = new Rectangle(box.X + 3, box.Y + 3, box.Width - 6, box.Height - 6);
                spriteBatch.Draw(pixel, inner, new Color(0, 200, 0, 220));
            }
            if (font != null)
                spriteBatch.DrawString(font, label, new Vector2(box.Right + 6, box.Y - 2), Color.White);
        }
    }
}
