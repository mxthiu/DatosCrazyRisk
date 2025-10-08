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

// === Integraciones ===
using CoreCardsService = CrazyRisk.Core.CardsService;
using CrazyRiskGame.Play.Adapters;
using CrazyRiskGame.Game.Services;
using CrazyRiskGame.Play.Controllers;
using CrazyRiskGame.Play.Animations;
using CrazyRiskGame.Play.UI;

namespace CrazyRiskGame
{
    public class Juego : Microsoft.Xna.Framework.Game
    {
        private GraphicsDeviceManager graficos;
        private SpriteBatch spriteBatch = null!;

        // ====== Tamaño / Layout general ======
        private const float MAP_SCALE = 2.0f;
        private const int UI_MARGIN_LEFT  = 20;
        private const int UI_MARGIN_RIGHT = 420;
        private const int UI_TOP_BAR      = 72;
        private const int UI_BOTTOM_BAR   = 72;

        // +++ NUEVO: agregamos estados MenuMultiplayer y Lobby
        private enum AppState { MenuPrincipal, MenuOpciones, MenuJugar, MenuPersonajes, MenuMultiplayer, Lobby, EnJuego }
        private AppState estado = AppState.MenuPrincipal;

        // ====== Assets ======
        private Texture2D? menuBg;
        private SpriteFont? font;
        private Texture2D pixel = null!;
        private Texture2D mapaVisible = null!;

        // ====== Botones ======
        private struct Button { public Rectangle Bounds; public string Text; public bool Hover; }
        private readonly List<Button> botonesMenu = new();
        private readonly List<Button> botonesOpciones = new();
        private readonly List<Button> botonesJugar = new();
        private readonly List<Button> botonesPersonajes = new();
        // +++ NUEVO: submenú Multiplayer
        private readonly List<Button> botonesMultiplayer = new();

        // ====== Selección de avatares ======
        private readonly List<Texture2D> avatarTex = new();
        private readonly List<Rectangle> avatarRects = new();
        private int selectedAvatarIndex = -1;
        private readonly Dictionary<int, int> avatarIndexByPlayer = new();

        // ====== Audio ======
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

        private readonly Random rng = new();

        // ====== Máscaras / Territorios ======

        private bool AreAdjacent(string fromId, string toId)
        {
            foreach (var n in NeighborsOf(fromId))
                if (n == toId) return true;
            return false;
        }
        private readonly Dictionary<string, Texture2D> maskPorId = new();
        private readonly Dictionary<string, Color[]> maskPixelsPorId = new();
        private readonly Dictionary<string, int> idToIndex = new();
        private readonly List<string> indexToId = new();
        private int[,] ownerGrid = new int[1,1];
        private bool[,] adj = new bool[1,1];

        private string? territorioHover = null;
        private string? territorioSeleccionado = null;
        private string? ultimoLogHover = null;

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

        // ====== Core (GameEngine) ======
        private GameEngine? engine;
        private readonly List<PlayerInfo> players = new()
        {
            new PlayerInfo(0, "Azul"),
            new PlayerInfo(1, "Rojo"),
            new PlayerInfo(2, "Verde"),
        };
        private readonly List<string> uiLog = new(64);

        // ====== Adapters / Services / Controllers / Animations ======
        private EngineAdapter? engineAdapter;
        private EngineActionsAdapter? actionsAdapter;
        private InputAdapter? input;

        private ReinforcementService? reinforcementService;
        private FortifyService? fortifyService;
        private AttackService? attackService;
        private SelectionService? selectionService;
        private TurnService? turnService;
        private ContinentBonusService? continentBonusService;
        private CoreCardsService? cardsService;

        private ReinforcementController? reinforcementController;
        private FortifyController? fortifyController;
        private MapSelectionController? mapSelectionController;
        private AttackController? attackController;

        private DiceAnimator? diceAnimator;

        // ====== UI EnJuego ======
        private enum SideTab { Refuerzos, Ataque, Movimiento, Cartas, Log }
        private SideTab sideTab = SideTab.Refuerzos;
        private bool sideCollapsed = false;

        private Rectangle rectTop, rectSide, rectBottom, rectSideHeader, rectMapViewport;

        private readonly List<Button> sideTabButtons = new();
        private Button sideCollapseButton;
        private readonly List<Button> bottomButtons = new();

        private const int SIDE_HEADER_H = 36;
        private const int TAB_BUTTON_W = 96;

        // ====== Refuerzos UI ======
        private int refuerzosStep = 1;
        private int refuerzosPendientes => engine?.State.ReinforcementsRemaining ?? 0;

        // ====== Ataque / Dados ======
        private Texture2D[] diceFaces = Array.Empty<Texture2D>();
        private enum DiceState { Idle, Rolling, Show }
        private DiceState diceState = DiceState.Idle;
        private double diceTimer = 0;
        private const double DICE_ROLL_DURATION = 0.8;
        private readonly int[] diceShown = new int[3];
        private readonly int[] diceShownDef = new int[2];
        private CrazyRisk.Core.DiceRollResult? lastRoll;

        // ====== Movimiento ======
        private int moveAmountSlider = 1;

        // ====== Selección local para ataque ======
        private string? attackFrom;
        private string? attackTo;

        // ====== Multiplayer (estados UI locales mínimos) ======
        private string playerName = "Jugador";
        private bool lobbyIsHost = false;
        private readonly List<string> lobbyPlayers = new(); // simple lista para UI
        private bool lobbyReadyToStart = false;

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

            // Texturas
            mapaVisible = Content.Load<Texture2D>("Sprites/map_marcado");
            try { menuBg = Content.Load<Texture2D>("Sprites/UI/menu_bg"); } catch { menuBg = null; }
            try { font = Content.Load<SpriteFont>("Fonts/Default"); } catch { font = null; }

            pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });

            // Máscaras
            maskPorId.Clear();
            maskPixelsPorId.Clear();
            idToIndex.Clear();
            indexToId.Clear();

            for (int i = 0; i < IDS_TERRITORIOS.Length; i++)
            {
                string id = IDS_TERRITORIOS[i];
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

            // Ventana (mapa + bordes UI)
            int mapW = (int)(mapaVisible.Width * MAP_SCALE);
            int mapH = (int)(mapaVisible.Height * MAP_SCALE);

            graficos.PreferredBackBufferWidth  = mapW + UI_MARGIN_LEFT + UI_MARGIN_RIGHT;
            graficos.PreferredBackBufferHeight = mapH + UI_TOP_BAR + UI_BOTTOM_BAR;
            graficos.ApplyChanges();

            // Rects base
            rectTop = new Rectangle(0, 0, graficos.PreferredBackBufferWidth, UI_TOP_BAR);
            rectBottom = new Rectangle(0, graficos.PreferredBackBufferHeight - UI_BOTTOM_BAR, graficos.PreferredBackBufferWidth, UI_BOTTOM_BAR);
            rectSide = new Rectangle(graficos.PreferredBackBufferWidth - UI_MARGIN_RIGHT, UI_TOP_BAR, UI_MARGIN_RIGHT, graficos.PreferredBackBufferHeight - UI_TOP_BAR - UI_BOTTOM_BAR);
            rectSideHeader = new Rectangle(rectSide.X, rectSide.Y, rectSide.Width, SIDE_HEADER_H);
            rectMapViewport = new Rectangle(UI_MARGIN_LEFT, UI_TOP_BAR, mapW, mapH);

            // Botón colapsar lateral
            sideCollapseButton = new Button
            {
                Bounds = new Rectangle(rectSide.X - 24, rectSide.Y + 8, 24, 28),
                Text = "<",
                Hover = false
            };

            // Tabs
            RebuildSideTabs();

            // Bottom buttons
            bottomButtons.Clear();
            string[] bb = { "Confirmar", "Deshacer", "Cancelar", "Siguiente" };
            int bw = 160, bh = 44, gap = 12, startX = 12;
            for (int i = 0; i < bb.Length; i++)
            {
                var r = new Rectangle(startX + i*(bw+gap), rectBottom.Y + (rectBottom.Height - bh)/2, bw, bh);
                bottomButtons.Add(new Button { Bounds = r, Text = bb[i], Hover = false });
            }

            // Música
            try { musicMenuFx = Content.Load<SoundEffect>("Audio/Music/music_menu"); } catch { musicMenuFx = null; }
            musicGameFx.Clear();
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game1")); } catch { musicGameFx.Add(null); }
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game2")); } catch { musicGameFx.Add(null); }
            try { musicGameFx.Add(Content.Load<SoundEffect>("Audio/Music/music_game3")); } catch { musicGameFx.Add(null); }

            CargarConfig();
            AplicarConfigVisual();
            if (!menuMusicStarted || currentMusic == null)
            {
                AplicarConfigAudio(estadoActualEsMenu: true);
                menuMusicStarted = true;
            }

            // Avatares
            avatarTex.Clear();
            for (int i = 1; i <= 6; i++)
            {
                try { avatarTex.Add(Content.Load<Texture2D>($"Sprites/avatars/perso{i}")); } catch { }
            }
            RebuildMenus();

            // Dados (caras)
            var faces = new List<Texture2D>();
            for (int i = 1; i <= 6; i++)
            {
                try { faces.Add(Content.Load<Texture2D>($"Sprites/Dice/Dice_{i}")); } catch { }
            }
            diceFaces = faces.ToArray();

            for (int i = 0; i < diceShown.Length; i++) diceShown[i] = 1;
            for (int i = 0; i < diceShownDef.Length; i++) diceShownDef[i] = 1;
        }

        // ======== Config ========
        private void CargarConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var c = JsonSerializer.Deserialize<Config>(json);
                    if (c != null) cfg = c;
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
            try { currentMusic?.Stop(); currentMusic?.Dispose(); } catch { }
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
                var ds = new List<SoundEffect>();
                foreach (var s in musicGameFx) if (s != null) ds.Add(s!);
                if (ds.Count > 0) PlayMusic(ds[rng.Next(ds.Count)]);
                else StopMusic();
            }
        }

        // ======== Menús ========
        private void RebuildMenus()
        {
            botonesMenu.Clear();
            botonesOpciones.Clear();
            botonesJugar.Clear();
            botonesPersonajes.Clear();
            botonesMultiplayer.Clear();
            avatarRects.Clear();

            int W = graficos.PreferredBackBufferWidth;
            int H = graficos.PreferredBackBufferHeight;

            // Menú principal (agregamos 4ta opción: Multiplayer)
            int bw = (int)(W * 0.28f);
            int bh = 46;
            int cx = W / 2 - bw / 2;
            int gap = 12;
            int totalH = bh * 4 + gap * 3;
            int startY = H / 2 - totalH / 2;

            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 0, bw, bh), Text = "Jugar" });
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 1, bw, bh), Text = "Opciones" });
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 2, bw, bh), Text = "Multiplayer (LAN)" });
            botonesMenu.Add(new Button { Bounds = new Rectangle(cx, startY + (bh + gap) * 3, bw, bh), Text = "Salir" });

            // Opciones
            int oy = H / 2 - (bh * 5 + gap * 4) / 2;
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 0, bw, bh), Text = $"Pantalla completa: {(cfg.Fullscreen ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 1, bw, bh), Text = $"Musica: {(cfg.MusicEnabled ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 2, (bw - gap) / 2, bh), Text = "Vol -" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx + (bw + gap) / 2, oy + (bh + gap) * 2, (bw - gap) / 2, bh), Text = "Vol +" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 3, bw, bh), Text = $"SFX: {(cfg.SfxEnabled ? "ON" : "OFF")}" });
            botonesOpciones.Add(new Button { Bounds = new Rectangle(cx, oy + (bh + gap) * 4, bw, bh), Text = "Volver" });

            // Jugar
            int jy = H / 2 - (bh * 2 + gap) / 2;
            botonesJugar.Add(new Button { Bounds = new Rectangle(cx, jy + (bh + gap) * 0, bw, bh), Text = "Partida rapida" });
            botonesJugar.Add(new Button { Bounds = new Rectangle(cx, jy + (bh + gap) * 1, bw, bh), Text = "Volver" });

            // Personajes (grid 3x2)
            int cols = 3, rows = 2;
            int cellW = (int)(W * 0.16f);
            int cellH = (int)(H * 0.22f);
            int gapX = (int)(W * 0.035f);
            int gapY = (int)(H * 0.035f);
            int gridW = cols * cellW + (cols - 1) * gapX;
            int gridH = rows * cellH + (rows - 1) * gapY;
            int gx = (W - gridW) / 2;
            int gy = (int)(H * 0.2f);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                if (i >= avatarTex.Count) break;
                var rect = new Rectangle(gx + c*(cellW+gapX), gy + r*(cellH+gapY), cellW, cellH);
                avatarRects.Add(rect);
            }
            int bw2 = (int)(W * 0.26f);
            int baseY = gy + gridH + 30;
            botonesPersonajes.Add(new Button { Bounds = new Rectangle(W/2 - bw2/2, baseY, bw2, 56), Text = "Confirmar" });
            botonesPersonajes.Add(new Button { Bounds = new Rectangle(W/2 - bw2/2, baseY + 72, bw2, 56), Text = "Volver" });

            // Multiplayer (LAN) submenu
            int my = H / 2 - (bh * 3 + gap * 2) / 2;
            botonesMultiplayer.Add(new Button { Bounds = new Rectangle(cx, my + (bh + gap) * 0, bw, bh), Text = "Crear lobby (host)" });
            botonesMultiplayer.Add(new Button { Bounds = new Rectangle(cx, my + (bh + gap) * 1, bw, bh), Text = "Unirse a lobby" });
            botonesMultiplayer.Add(new Button { Bounds = new Rectangle(cx, my + (bh + gap) * 2, bw, bh), Text = "Volver" });
        }

        private void RebuildSideTabs()
        {
            sideTabButtons.Clear();
            if (!sideCollapsed)
            {
                string[] tabs = { "Refuerzos", "Ataque", "Movimiento", "Cartas", "Log" };
                for (int i = 0; i < tabs.Length; i++)
                {
                    int x = rectSide.X + 8 + i * (TAB_BUTTON_W + 6);
                    var r = new Rectangle(x, rectSideHeader.Y + 4, TAB_BUTTON_W, rectSideHeader.Height - 8);
                    sideTabButtons.Add(new Button { Bounds = r, Text = tabs[i], Hover = false });
                }
            }
        }

        // ======== Mapa / adyacencias ========
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

        // ======== Estado / Transiciones ========
        private void CambiarEstado(AppState nuevo)
        {
            if (nuevo == AppState.EnJuego) AplicarConfigAudio(false);
            else
            {
                if (!menuMusicStarted || currentMusic == null)
                {
                    AplicarConfigAudio(true);
                    menuMusicStarted = true;
                }
            }

            estado = nuevo;

            if (estado == AppState.EnJuego)
            {
                if (mapaCore == null)
                {
                    ExportarTerritoriesJson();
                    CargarMapaCoreSiExiste();
                }

                if (mapaCore == null)
                {
                    uiLog.Add("No se pudo cargar el mapa de datos (Content/Data/territories.json).");
                }
                else
                {
                    engine = new GameEngine(mapaCore, players);

                    engineAdapter = new EngineAdapter(engine);
                    actionsAdapter = new EngineActionsAdapter(engine);
                    input = new InputAdapter();

                    selectionService       = new SelectionService(engine!);
                    reinforcementService   = new ReinforcementService(engine!);
                    fortifyService         = new FortifyService(engine!);
                    try { attackService    = new AttackService(engine!); } catch { attackService = null; }
                    turnService            = new TurnService(engine!);
                    continentBonusService  = null;
                    // cardsService        = new CardsService();

                    reinforcementController = new ReinforcementController(engine!);
                    fortifyController       = new FortifyController(engine!);
                    mapSelectionController  = null;

                    attackController        = new AttackController(engine!, selectionService, msg => uiLog.Add(msg));
                    diceAnimator            = new DiceAnimator();

                    uiLog.Clear();
                    uiLog.Add("Comienza la partida. Fase: Refuerzos.");
                }

                territorioSeleccionado = null;
                attackFrom = null;
                attackTo   = null;

                sideTab = SideTab.Refuerzos;
                sideCollapsed = false;
                RebuildSideTabs();

                diceState = DiceState.Idle;
                diceTimer = 0;
                for (int i = 0; i < diceShown.Length; i++) diceShown[i] = 1;
                for (int i = 0; i < diceShownDef.Length; i++) diceShownDef[i] = 1;
                lastRoll = null;
            }
        }

        // ======== Update ========
        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            var mouse = Mouse.GetState();
            var pos = new Point(mouse.X, mouse.Y);

            input?.Capture();

            // ESC navegación
            if (kb.IsKeyDown(Keys.Escape) && !prevKb.IsKeyDown(Keys.Escape))
            {
                switch (estado)
                {
                    case AppState.MenuOpciones:
                    case AppState.MenuJugar:
                    case AppState.MenuPersonajes:
                    case AppState.MenuMultiplayer:
                        CambiarEstado(AppState.MenuPrincipal);
                        break;
                    case AppState.Lobby:
                        // salir del lobby vuelve al submenú Multiplayer
                        CambiarEstado(AppState.MenuMultiplayer);
                        break;
                    case AppState.EnJuego:
                        CambiarEstado(AppState.MenuPrincipal);
                        break;
                    case AppState.MenuPrincipal:
                        Exit();
                        break;
                }
            }

            if (estado == AppState.MenuPrincipal || estado == AppState.MenuOpciones || estado == AppState.MenuJugar || estado == AppState.MenuPersonajes || estado == AppState.MenuMultiplayer)
            {
                UpdateMenus(kb, mouse, pos);
                prevKb = kb;
                prevMouse = mouse;
                base.Update(gameTime);
                return;
            }

            if (estado == AppState.Lobby)
            {
                UpdateLobby(mouse, pos);
                prevKb = kb;
                prevMouse = mouse;
                base.Update(gameTime);
                return;
            }

            // ===== En Juego =====

            sideCollapseButton.Hover = sideCollapseButton.Bounds.Contains(pos);
            if (Clicked(sideCollapseButton.Bounds, mouse))
            {
                sideCollapsed = !sideCollapsed;
                sideCollapseButton.Text = sideCollapsed ? ">" : "<";
                RebuildSideTabs();
            }

            if (!sideCollapsed)
            {
                for (int i = 0; i < sideTabButtons.Count; i++)
                {
                    var b = sideTabButtons[i];
                    b.Hover = b.Bounds.Contains(pos);
                    sideTabButtons[i] = b;

                    if (Clicked(b.Bounds, mouse))
                        sideTab = (SideTab)i;
                }
            }

            for (int i = 0; i < bottomButtons.Count; i++)
            {
                var b = bottomButtons[i];
                b.Hover = b.Bounds.Contains(pos);
                bottomButtons[i] = b;

                if (Clicked(b.Bounds, mouse))
                    OnBottomButtonClick(b.Text);
            }

            if (kb.IsKeyDown(Keys.J) && !prevKb.IsKeyDown(Keys.J))
            {
                ExportarTerritoriesJson();
                CargarMapaCoreSiExiste();
            }

            bool sobreMapa = rectMapViewport.Contains(pos);
            if (sobreMapa)
            {
                int mx = (int)((mouse.X - rectMapViewport.X) / MAP_SCALE);
                int my = (int)((mouse.Y - rectMapViewport.Y) / MAP_SCALE);

                territorioHover = DetectarTerritorioPorGris(mx, my);
                if (territorioHover != ultimoLogHover)
                {
                    Console.WriteLine(territorioHover ?? "SinTerritorio");
                    ultimoLogHover = territorioHover;
                }

                if (Clicked(new Rectangle(mouse.X, mouse.Y, 1, 1), mouse))
                    OnMapLeftClick(mx, my);

                if (mouse.RightButton == ButtonState.Pressed && prevMouse.RightButton == ButtonState.Released)
                {
                    territorioSeleccionado = null;
                    attackFrom = null;
                    attackTo   = null;
                }
            }

            diceAnimator?.Update(gameTime);

            if (diceState == DiceState.Rolling)
            {
                diceTimer += gameTime.ElapsedGameTime.TotalSeconds;
                if (diceTimer < DICE_ROLL_DURATION)
                {
                    if (gameTime.TotalGameTime.Milliseconds % 60 < 20)
                    {
                        for (int i = 0; i < diceShown.Length; i++) diceShown[i] = rng.Next(1, 7);
                        for (int i = 0; i < diceShownDef.Length; i++) diceShownDef[i] = rng.Next(1, 7);
                    }
                }
                else
                {
                    diceState = DiceState.Show;
                    if (lastRoll != null)
                    {
                        for (int i = 0; i < diceShown.Length; i++)
                            diceShown[i] = i < lastRoll.AttackerDice.Length ? lastRoll.AttackerDice[i] : 1;
                        for (int i = 0; i < diceShownDef.Length; i++)
                            diceShownDef[i] = i < lastRoll.DefenderDice.Length ? lastRoll.DefenderDice[i] : 1;
                    }
                }
            }

            prevKb = kb;
            prevMouse = mouse;
            base.Update(gameTime);
        }
        // ======== Botones de la barra inferior ========
        private void OnBottomButtonClick(string text)
        {
            if (engine == null) return;

            switch (text)
            {
                case "Confirmar":
                    uiLog.Add("Confirmar pulsado.");
                    break;

                case "Deshacer":
                    uiLog.Add("Deshacer pulsado (sin pila de undo).");
                    break;

                case "Cancelar":
                    territorioSeleccionado = null;
                    attackFrom = null;
                    attackTo = null;
                    uiLog.Add("Cancelado.");
                    diceState = DiceState.Idle;
                    break;

                case "Siguiente":
                    engine!.NextPhaseOrTurn();
                    uiLog.Add("Siguiente: " + engine.State.Phase.ToString());
                    sideTab = engine.State.Phase switch
                    {
                        Phase.Reinforcement => SideTab.Refuerzos,
                        Phase.Attack => SideTab.Ataque,
                        _ => SideTab.Movimiento
                    };

                    // reset selecciones transversales por fase
                    if (engine.State.Phase != Phase.Attack) { attackFrom = attackTo = null; }
                    if (engine.State.Phase != Phase.Fortify) { territorioSeleccionado = null; }

                    break;
            }
        }
        // ======== Click izquierdo sobre el mapa ========
        private void OnMapLeftClick(int mx, int my)
        {
            if (engine == null) return;

            string? hit = DetectarTerritorioPorGris(mx, my);
            if (hit == null) return;

            switch (engine.State.Phase)
            {
                case Phase.Reinforcement:
                    if (engine.State.Territories.TryGetValue(hit, out var t1) && t1.OwnerId == engine.State.CurrentPlayerId)
                    {
                        territorioSeleccionado = hit;
                        uiLog.Add("Seleccionado para refuerzos: " + hit);
                    }
                    else
                    {
                        uiLog.Add("[INFO] Selecciona un territorio propio para refuerzos.");
                    }
                    break;

                case Phase.Attack:
                    {
                        // Primer click: atacante propio (>1 tropas). Segundo: defensor enemigo adyacente.
                        if (attackFrom == null)
                        {
                            if (engine.State.Territories.TryGetValue(hit, out var fromT)
                                && fromT.OwnerId == engine.State.CurrentPlayerId
                                && fromT.Troops > 1)
                            {
                                attackFrom = hit;
                                attackTo = null;
                                uiLog.Add("Atacante seleccionado: " + attackFrom);
                            }
                            else
                            {
                                uiLog.Add("[INFO] Selecciona un territorio propio con >1 tropas para atacar.");
                            }
                        }
                        else
                        {
                            if (hit == attackFrom)
                            {
                                attackFrom = null;
                                attackTo = null;
                                uiLog.Add("Atacante deseleccionado.");
                            }
                            else
                            {
                                if (engine.State.Territories.TryGetValue(hit, out var t)
                                    && t.OwnerId != engine.State.CurrentPlayerId
                                    && AreAdjacent(attackFrom!, hit))
                                {
                                    attackTo = hit;
                                    uiLog.Add("Objetivo seleccionado: " + attackTo);
                                }
                                else
                                {
                                    uiLog.Add("[INFO] El defensor debe ser enemigo y adyacente.");
                                }
                            }
                        }
                        break;
                    }

                case Phase.Fortify:
                    {
                        // Primer click: origen propio (>1). Segundo: destino propio adyacente
                        if (territorioSeleccionado == null)
                        {
                            if (engine.State.Territories.TryGetValue(hit, out var from)
                                && from.OwnerId == engine.State.CurrentPlayerId
                                && from.Troops > 1)
                            {
                                territorioSeleccionado = hit;
                                uiLog.Add("Fortificar origen: " + hit);
                            }
                            else
                            {
                                uiLog.Add("[INFO] Selecciona un territorio propio con >1 tropas como origen.");
                            }
                        }
                        else
                        {
                            if (hit == territorioSeleccionado)
                            {
                                territorioSeleccionado = null;
                                uiLog.Add("Fortificar: origen deseleccionado.");
                            }
                            else if (engine.State.Territories.TryGetValue(hit, out var to)
                                     && to.OwnerId == engine.State.CurrentPlayerId
                                     && AreAdjacent(territorioSeleccionado!, hit))
                            {
                                int move = Math.Max(1, moveAmountSlider);
                                // Simulación: aquí iría tu llamada real a FortifyService cuando lo conectes.
                                uiLog.Add($"[SIM] Fortificar: {territorioSeleccionado} -> {hit} ({move})");
                                territorioSeleccionado = null;
                            }
                            else
                            {
                                territorioSeleccionado = null;
                                uiLog.Add("[INFO] El destino debe ser propio y adyacente al origen.");
                            }
                        }
                        break;
                    }
            }
        }

        private void UpdateMenus(KeyboardState kb, MouseState mouse, Point pos)
        {
            var lista = estado switch
            {
                AppState.MenuOpciones => botonesOpciones,
                AppState.MenuJugar => botonesJugar,
                AppState.MenuPersonajes => botonesPersonajes,
                AppState.MenuMultiplayer => botonesMultiplayer,
                _ => botonesMenu
            };

            if (estado != AppState.MenuPersonajes)
            {
                for (int i = 0; i < lista.Count; i++)
                {
                    var b = lista[i];
                    b.Hover = b.Bounds.Contains(pos);

                    if (estado == AppState.MenuOpciones) botonesOpciones[i] = b;
                    else if (estado == AppState.MenuJugar) botonesJugar[i] = b;
                    else if (estado == AppState.MenuMultiplayer) botonesMultiplayer[i] = b;
                    else botonesMenu[i] = b;
                }
            }
            else
            {
                for (int i = 0; i < botonesPersonajes.Count; i++)
                {
                    var b = botonesPersonajes[i];
                    b.Hover = b.Bounds.Contains(pos);
                    botonesPersonajes[i] = b;
                }
            }

            if (mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released)
            {
                if (estado == AppState.MenuPrincipal)
                {
                    if (botonesMenu[0].Bounds.Contains(pos)) { CambiarEstado(AppState.MenuJugar); }
                    else if (botonesMenu[1].Bounds.Contains(pos)) { CambiarEstado(AppState.MenuOpciones); }
                    else if (botonesMenu[2].Bounds.Contains(pos)) { CambiarEstado(AppState.MenuMultiplayer); }
                    else if (botonesMenu[3].Bounds.Contains(pos)) { Exit(); }
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

                    var b0 = botonesOpciones[0]; b0.Text = $"Pantalla completa: {(cfg.Fullscreen ? "ON" : "OFF")}"; botonesOpciones[0] = b0;
                    var b1 = botonesOpciones[1]; b1.Text = $"Musica: {(cfg.MusicEnabled ? "ON" : "OFF")}"; botonesOpciones[1] = b1;
                    var b4 = botonesOpciones[4]; b4.Text = $"SFX: {(cfg.SfxEnabled ? "ON" : "OFF")}"; botonesOpciones[4] = b4;
                }
                else if (estado == AppState.MenuJugar)
                {
                    if (botonesJugar[0].Bounds.Contains(pos))
                    { selectedAvatarIndex = -1; CambiarEstado(AppState.MenuPersonajes); }
                    else if (botonesJugar[1].Bounds.Contains(pos))
                    { CambiarEstado(AppState.MenuPrincipal); }
                }
                else if (estado == AppState.MenuPersonajes)
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
                else if (estado == AppState.MenuMultiplayer)
                {
                    if (botonesMultiplayer[0].Bounds.Contains(pos))
                    {
                        // Crear lobby local (host)
                        lobbyIsHost = true;
                        lobbyPlayers.Clear();
                        lobbyPlayers.Add(playerName + " (Host)");
                        lobbyReadyToStart = false;
                        estado = AppState.Lobby;
                    }
                    else if (botonesMultiplayer[1].Bounds.Contains(pos))
                    {
                        // Unirse a lobby (cliente) - mock
                        lobbyIsHost = false;
                        lobbyPlayers.Clear();
                        lobbyPlayers.Add("Host");
                        lobbyPlayers.Add(playerName);
                        lobbyReadyToStart = false;
                        estado = AppState.Lobby;
                    }
                    else if (botonesMultiplayer[2].Bounds.Contains(pos))
                    {
                        CambiarEstado(AppState.MenuPrincipal);
                    }
                }
            }
        }

        // ======== Lobby simple (UI local, sin red todavía) ========
        private void UpdateLobby(MouseState mouse, Point pos)
        {
            // definimos rects botones dentro del lobby
            int W = graficos.PreferredBackBufferWidth;
            int H = graficos.PreferredBackBufferHeight;

            int bw = (int)(W * 0.24f);
            int bh = 42;
            int cx = W / 2 - bw / 2;
            int baseY = (int)(H * 0.70f);
            var btnVolver  = new Rectangle(cx, baseY, bw, bh);
            var btnIniciar = new Rectangle(cx, baseY - (bh + 12), bw, bh);

            // hover no persistente (solo para dibujado)
            bool hovBack = btnVolver.Contains(pos);
            bool hovStart = btnIniciar.Contains(pos);

            if (Clicked(btnVolver, mouse))
            {
                CambiarEstado(AppState.MenuMultiplayer);
                return;
            }

            if (Clicked(btnIniciar, mouse))
            {
                // Solo puede iniciar el host, y con 2-3 jugadores
                if (lobbyIsHost && lobbyPlayers.Count >= 2)
                {
                    lobbyReadyToStart = true;
                    CambiarEstado(AppState.EnJuego);
                }
            }
        }

        // ======== Detección territorio ========
        private string? DetectarTerritorioPorGris(int x, int y)
        {
            if (x < 0 || y < 0) return null;

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

        // ======== Export ========
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

        // ======== Draw ========
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Lobby: pantalla dedicada
            if (estado == AppState.Lobby)
            {
                DrawLobby();
                spriteBatch.End();
                base.Draw(gameTime);
                return;
            }

            if (estado != AppState.EnJuego)
            {
                DrawMenus();
                spriteBatch.End();
                base.Draw(gameTime);
                return;
            }

            // Fondo UI
            spriteBatch.Draw(pixel, rectTop, new Color(15, 15, 18, 255));
            spriteBatch.Draw(pixel, rectBottom, new Color(15, 15, 18, 255));
            spriteBatch.Draw(pixel, rectSide, new Color(15, 15, 18, 225));
            DrawRect(rectTop, new Color(255,255,255,40), 1);
            DrawRect(rectBottom, new Color(255,255,255,40), 1);
            DrawRect(rectSide, new Color(255,255,255,40), 1);

            // Mapa
            var mapPos = new Vector2(rectMapViewport.X, rectMapViewport.Y);
            spriteBatch.Draw(mapaVisible, mapPos, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);

            // Máscara hover / selección
            if (territorioSeleccionado != null && maskPorId.TryGetValue(territorioSeleccionado, out var selMask))
                spriteBatch.Draw(selMask, mapPos, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);
            else if (territorioHover != null && maskPorId.TryGetValue(territorioHover, out var hovMask))
                spriteBatch.Draw(hovMask, mapPos, null, Color.White, 0f, Vector2.Zero, MAP_SCALE, SpriteEffects.None, 0f);

            // HUD superior
            if (font != null && engine != null)
            {
                string hud = $"Jugador: {engine.State.CurrentPlayerId}   Fase: {engine.State.Phase}";
                spriteBatch.DrawString(font, hud, new Vector2(rectTop.X + 12, rectTop.Y + 12), Color.White);

                if (engine.State.Phase == Phase.Reinforcement)
                {
                    string rf = $"Refuerzos: {engine.State.ReinforcementsRemaining}";
                    spriteBatch.DrawString(font, rf, new Vector2(rectTop.Right - 12 - font.MeasureString(rf).X, rectTop.Y + 12), Color.White);
                }
            }

            // Panel lateral
            sideCollapseButton.Bounds = new Rectangle(rectSide.X - 24, rectSide.Y + 8, 24, 28);
            var colBg = sideCollapseButton.Hover ? new Color(255,255,255,220) : new Color(255,255,255,180);
            spriteBatch.Draw(pixel, sideCollapseButton.Bounds, colBg);
            DrawRect(sideCollapseButton.Bounds, new Color(0,0,0,200), 2);
            if (font != null)
            {
                var ts = font.MeasureString(sideCollapseButton.Text);
                spriteBatch.DrawString(font, sideCollapseButton.Text,
                    new Vector2(sideCollapseButton.Bounds.X + (sideCollapseButton.Bounds.Width - ts.X) * 0.5f,
                                sideCollapseButton.Bounds.Y + (sideCollapseButton.Bounds.Height - ts.Y) * 0.5f),
                    Color.Black);
            }

            if (!sideCollapsed)
            {
                spriteBatch.Draw(pixel, rectSideHeader, new Color(255,255,255,20));
                DrawRect(rectSideHeader, new Color(255,255,255,60), 1);

                for (int i = 0; i < sideTabButtons.Count; i++)
                {
                    var b = sideTabButtons[i];
                    var active = (i == (int)sideTab);
                    var bgc = active ? new Color(255,255,255,220) : (b.Hover ? new Color(255,255,255,160) : new Color(255,255,255,110));
                    spriteBatch.Draw(pixel, b.Bounds, bgc);
                    DrawRect(b.Bounds, new Color(0,0,0,200), active ? 2 : 1);
                    if (font != null)
                    {
                        var s = font.MeasureString(b.Text);
                        spriteBatch.DrawString(font, b.Text, new Vector2(b.Bounds.X + (b.Bounds.Width - s.X) * 0.5f, b.Bounds.Y + (b.Bounds.Height - s.Y) * 0.5f), Color.Black);
                    }
                }

                var content = new Rectangle(rectSide.X + 10, rectSide.Y + SIDE_HEADER_H + 10, rectSide.Width - 20, rectSide.Height - SIDE_HEADER_H - 20);
                DrawSideContent(content);
            }

            // Bottom bar
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

            // Overlay info selección
            if (font != null)
            {
                string sel = territorioSeleccionado ?? "(ninguno)";
                string hov = territorioHover ?? "(ninguno)";
                var measure = Math.Max(font.MeasureString(sel).X, font.MeasureString(hov).X);
                var box = new Rectangle(rectMapViewport.X, rectBottom.Y - 56, (int)Math.Max(280, measure + 28), 48);
                spriteBatch.Draw(pixel, box, new Color(0,0,0,140));
                DrawRect(box, new Color(255,255,255,120), 1);
                spriteBatch.DrawString(font, $"Sel: {sel}", new Vector2(box.X + 10, box.Y + 8), Color.White);
                spriteBatch.DrawString(font, $"Hover: {hov}", new Vector2(box.X + 10, box.Y + 26), Color.White);
            }

            spriteBatch.End();
            base.Draw(gameTime);
        }

        // ======== Lobby Draw ========
        private void DrawLobby()
        {
            var W = graficos.PreferredBackBufferWidth;
            var H = graficos.PreferredBackBufferHeight;

            // fondo
            var bg = menuBg ?? mapaVisible;
            var scale = ComputeScaleToFit(bg.Width, bg.Height, W, H);
            var pos = new Vector2((W - bg.Width * scale) * 0.5f, (H - bg.Height * scale) * 0.5f);
            spriteBatch.Draw(bg, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            if (font != null)
            {
                string titulo = lobbyIsHost ? "Lobby (Host)" : "Lobby (Cliente)";
                var size = font.MeasureString(titulo);
                spriteBatch.DrawString(font, titulo, new Vector2((W - size.X) * 0.5f, 40), Color.White);

                // caja jugadores
                var box = new Rectangle(W/2 - 300, 120, 600, 320);
                spriteBatch.Draw(pixel, box, new Color(0,0,0,140));
                DrawRect(box, Color.White, 2);

                int y = box.Y + 16;
                spriteBatch.DrawString(font, "Jugadores:", new Vector2(box.X + 16, y), Color.White);
                y += 28;

                if (lobbyPlayers.Count == 0)
                {
                    spriteBatch.DrawString(font, "(vacio... esperando jugadores en la LAN)", new Vector2(box.X + 16, y), Color.White);
                    y += 22;
                }
                else
                {
                    foreach (var p in lobbyPlayers)
                    {
                        spriteBatch.DrawString(font, "• " + p, new Vector2(box.X + 16, y), Color.White);
                        y += 22;
                    }
                }

                // botones
                int bw = 280, bh = 42;
                int cx = W / 2 - bw / 2;
                int baseY = (int)(H * 0.70f);
                var btnIniciar = new Rectangle(cx, baseY - (bh + 12), bw, bh);
                var btnVolver  = new Rectangle(cx, baseY, bw, bh);

                DrawUIButton(btnIniciar, lobbyIsHost ? "Iniciar partida" : "(esperando host)");
                DrawUIButton(btnVolver, "Volver");
            }
        }

        private void DrawUIButton(Rectangle r, string text)
        {
            spriteBatch.Draw(pixel, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0,0,0,80));
            spriteBatch.Draw(pixel, r, new Color(255,255,255,170));
            DrawRect(r, new Color(0,0,0,200), 2);
            if (font != null)
            {
                var size = font.MeasureString(text);
                spriteBatch.DrawString(font, text, new Vector2(r.X + (r.Width - size.X) * 0.5f, r.Y + (r.Height - size.Y) * 0.5f), Color.Black);
            }
        }

        // ======== Side Content ========
        private void DrawSideContent(Rectangle area)
        {
            DrawRect(area, new Color(255,255,255,60), 1);
            if (font == null) return;

            switch (sideTab)
            {
                case SideTab.Refuerzos:
                {
                    spriteBatch.DrawString(font, "Refuerzos", new Vector2(area.X + 6, area.Y + 6), Color.White);

                    var r1 = new Rectangle(area.X + 6, area.Y + 32, area.Width - 12, 40);
                    spriteBatch.Draw(pixel, r1, new Color(255,255,255,20));
                    DrawRect(r1, new Color(255,255,255,60), 1);
                    string rf = $"Pendientes: {refuerzosPendientes}";
                    spriteBatch.DrawString(font, rf, new Vector2(r1.X + 8, r1.Y + 10), Color.White);

                    var btnMinus = new Rectangle(r1.Right - 160, r1.Y + 6, 36, 28);
                    var btnPlus  = new Rectangle(r1.Right - 120, r1.Y + 6, 36, 28);
                    var stepBox  = new Rectangle(r1.Right - 76, r1.Y + 6, 64, 28);
                    DrawUITextButton(btnMinus, "-");
                    DrawUITextButton(btnPlus , "+");
                    spriteBatch.Draw(pixel, stepBox, new Color(255,255,255,100));
                    DrawRect(stepBox, new Color(0,0,0,180), 1);
                    var s = font!.MeasureString(refuerzosStep.ToString());
                    spriteBatch.DrawString(font, refuerzosStep.ToString(),
                        new Vector2(stepBox.X + (stepBox.Width - s.X)/2, stepBox.Y + (stepBox.Height - s.Y)/2), Color.Black);

                    var mouse = Mouse.GetState();
                    if (mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released)
                    {
                        var p = new Point(mouse.X, mouse.Y);
                        if (btnMinus.Contains(p)) refuerzosStep = Math.Max(1, refuerzosStep - 1);
                        if (btnPlus.Contains(p))  refuerzosStep = Math.Min(20, refuerzosStep + 1);
                    }

                    var r2 = new Rectangle(area.X + 6, r1.Bottom + 10, area.Width - 12, 40);
                    DrawUIPrimaryButton(r2, "Colocar en territorio seleccionado");

                    var mouseNow = Mouse.GetState();
                    if (Clicked(r2, mouseNow))
                    {
                        if (engine == null) { uiLog.Add("[ERR] Engine no inicializado."); }
                        else if (engine.State.Phase != Phase.Reinforcement) { uiLog.Add("[WARN] No estás en fase de refuerzos."); }
                        else if (territorioSeleccionado == null) { uiLog.Add("[INFO] Selecciona un territorio propio primero."); }
                        else
                        {
                            int place = Math.Min(refuerzosStep, refuerzosPendientes);
                            if (place <= 0) { uiLog.Add("[INFO] No hay refuerzos disponibles."); }
                            else
                            {
                                uiLog.Add($"[SIM] +{place} en {territorioSeleccionado} (conecta ReinforcementService para aplicar de verdad).");
                            }
                        }
                    }

                    break;
                }

                case SideTab.Ataque:
                {
                    spriteBatch.DrawString(font, "Ataque", new Vector2(area.X + 6, area.Y + 6), Color.White);

                    var attBox = new Rectangle(area.X + 6, area.Y + 30, area.Width - 12, 54);
                    var defBox = new Rectangle(area.X + 6, attBox.Bottom + 6, area.Width - 12, 54);
                    spriteBatch.Draw(pixel, attBox, new Color(255,255,255,20));
                    spriteBatch.Draw(pixel, defBox, new Color(255,255,255,20));
                    DrawRect(attBox, new Color(255,255,255,60), 1);
                    DrawRect(defBox, new Color(255,255,255,60), 1);

                    string att = attackFrom ?? "(elige atacante)";
                    string def = attackTo   ?? "(elige defensor)";
                    spriteBatch.DrawString(font, $"Atacante: {att}", new Vector2(attBox.X + 8, attBox.Y + 8), Color.White);
                    spriteBatch.DrawString(font, $"Defensor : {def}", new Vector2(defBox.X + 8, defBox.Y + 8), Color.White);

                    var btn = new Rectangle(area.X + 6, defBox.Bottom + 10, area.Width - 12, 40);
                    DrawUIPrimaryButton(btn, "Lanzar dados");
                    var mouseNow2 = Mouse.GetState();
                    if (Clicked(btn, mouseNow2))
                    {
                        if (engine == null) { uiLog.Add("[ERR] Engine no inicializado."); }
                        else if (engine.State.Phase != Phase.Attack) { uiLog.Add("[WARN] No estás en fase de ataque."); }
                        else if (attackFrom == null || attackTo == null) { uiLog.Add("[INFO] Selecciona atacante y defensor (click en mapa)."); }
                        else
                        {
                            TryRollDiceFromEngine();
                            if (lastRoll != null && lastRoll.TerritoryCaptured)
                            {
                                attackFrom = null;
                                attackTo   = null;
                            }
                        }
                    }

                    var strip = new Rectangle(area.X + 6, btn.Bottom + 8, area.Width - 12, 72);
                    DrawRect(strip, new Color(255,255,255,40), 1);

                    if (diceFaces.Length >= 6)
                    {
                        int nA = 3;
                        int nD = 2;
                        int gap = 10;
                        int dieSize = Math.Min((strip.Height - 10), (strip.Width - gap * (nA + nD + 2)) / (nA + nD));
                        dieSize = Math.Max(28, dieSize);

                        int x = strip.X + gap;
                        int y = strip.Y + (strip.Height - dieSize) / 2;

                        for (int i = 0; i < nA; i++)
                        {
                            int val = Math.Clamp(diceShown[i], 1, 6);
                            var tex = diceFaces[val - 1];
                            float sc = MathF.Min((float)dieSize / tex.Width, (float)dieSize / tex.Height);
                            spriteBatch.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                            x += dieSize + gap;
                        }

                        x += gap;

                        for (int i = 0; i < nD; i++)
                        {
                            int val = Math.Clamp(diceShownDef[i], 1, 6);
                            var tex = diceFaces[val - 1];
                            float sc = MathF.Min((float)dieSize / tex.Width, (float)dieSize / tex.Height);
                            spriteBatch.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                            x += dieSize + gap;
                        }
                    }

                    if (lastRoll != null)
                    {
                        var txt = $"Resultado: A-{lastRoll.AttackerLosses}  D-{lastRoll.DefenderLosses}" + (lastRoll.TerritoryCaptured ? "  Capturado" : "");
                        spriteBatch.DrawString(font, txt, new Vector2(area.X + 6, strip.Bottom + 8), Color.White);
                    }

                    break;
                }

                case SideTab.Movimiento:
                {
                    spriteBatch.DrawString(font, "Movimiento", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var info = new Rectangle(area.X + 6, area.Y + 30, area.Width - 12, 48);
                    spriteBatch.Draw(pixel, info, new Color(255,255,255,18));
                    DrawRect(info, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "Click en origen propio, luego destino propio conectado.", new Vector2(info.X + 8, info.Y + 12), Color.White);

                    var row = new Rectangle(area.X + 6, info.Bottom + 8, area.Width - 12, 40);
                    spriteBatch.Draw(pixel, row, new Color(255,255,255,18));
                    DrawRect(row, new Color(255,255,255,60), 1);
                    var bMinus = new Rectangle(row.X + 8, row.Y + 6, 36, 28);
                    var bPlus  = new Rectangle(row.Right - 44, row.Y + 6, 36, 28);
                    DrawUITextButton(bMinus, "-");
                    DrawUITextButton(bPlus , "+");
                    var box = new Rectangle(bMinus.Right + 8, row.Y + 6, row.Width - (bMinus.Width + bPlus.Width + 32), 28);
                    spriteBatch.Draw(pixel, box, new Color(255,255,255,80));
                    DrawRect(box, new Color(0,0,0,180), 1);
                    var s = font!.MeasureString(moveAmountSlider.ToString());
                    spriteBatch.DrawString(font, moveAmountSlider.ToString(), new Vector2(box.X + (box.Width - s.X)/2, box.Y + (box.Height - s.Y)/2), Color.Black);

                    var mouse = Mouse.GetState();
                    if (mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released)
                    {
                        var p = mouse.Position;
                        if (bMinus.Contains(p)) moveAmountSlider = Math.Max(1, moveAmountSlider - 1);
                        if (bPlus.Contains(p))  moveAmountSlider = Math.Min(99, moveAmountSlider + 1);
                    }

                    break;
                }

                case SideTab.Cartas:
                {
                    spriteBatch.DrawString(font, "Cartas", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var grid = new Rectangle(area.X + 6, area.Y + 30, area.Width - 12, area.Height - 40);
                    spriteBatch.Draw(pixel, grid, new Color(255,255,255,18));
                    DrawRect(grid, new Color(255,255,255,60), 1);
                    spriteBatch.DrawString(font, "(Pendiente de implementar)", new Vector2(grid.X + 8, grid.Y + 8), Color.White);
                    break;
                }

                case SideTab.Log:
                {
                    spriteBatch.DrawString(font, "Log", new Vector2(area.X + 6, area.Y + 6), Color.White);
                    var box = new Rectangle(area.X + 6, area.Y + 28, area.Width - 12, area.Height - 34);
                    spriteBatch.Draw(pixel, box, new Color(255,255,255,16));
                    DrawRect(box, new Color(255,255,255,60), 1);

                    int y = box.Y + 8;
                    for (int i = Math.Max(0, uiLog.Count - 20); i < uiLog.Count; i++)
                    {
                        spriteBatch.DrawString(font!, uiLog[i], new Vector2(box.X + 8, y), Color.White);
                        y += 18;
                        if (y > box.Bottom - 20) break;
                    }
                    break;
                }
            }
        }

        private void TryRollDiceFromEngine()
        {
            if (engine == null || attackController == null)
            {
                uiLog.Add("No estas en fase de ataque.");
                return;
            }
            if (engine.State.Phase != Phase.Attack)
            {
                uiLog.Add("No estas en fase de ataque.");
                return;
            }

            if (!attackController.RollOnce(out CrazyRisk.Core.DiceRollResult? r, out string err) || r == null)
            {
                uiLog.Add("No se pudo tirar: " + err);
                return;
            }

            lastRoll = r;
            uiLog.Add($"Tiro: A[{string.Join(",", r.AttackerDice)}] vs D[{string.Join(",", r.DefenderDice)}]  Perdidas A:{r.AttackerLosses} D:{r.DefenderLosses}" + (r.TerritoryCaptured ? " CAPTURADO" : ""));

            diceState = DiceState.Rolling;
            diceTimer = 0;
            for (int i = 0; i < diceShown.Length; i++) diceShown[i] = rng.Next(1, 7);
            for (int i = 0; i < diceShownDef.Length; i++) diceShownDef[i] = rng.Next(1, 7);

            int aCount = r.AttackerDice?.Length ?? 0;
            int dCount = r.DefenderDice?.Length ?? 0;
            diceAnimator?.Start(aCount, dCount, DICE_ROLL_DURATION);
        }

        // ======== Helpers ========
        private bool Clicked(Rectangle r, MouseState curMouse)
        {
            return prevMouse.LeftButton == ButtonState.Released
                && curMouse.LeftButton == ButtonState.Pressed
                && r.Contains(curMouse.Position);
        }

        // ======== Util draw ========
        private void DrawMenus()
        {
            var W = graficos.PreferredBackBufferWidth;
            var H = graficos.PreferredBackBufferHeight;
            var bg = menuBg ?? mapaVisible;
            var scale = ComputeScaleToFit(bg.Width, bg.Height, W, H);
            var pos = new Vector2(
                (W - bg.Width * scale) * 0.5f,
                (H - bg.Height * scale) * 0.5f
            );
            spriteBatch.Draw(bg, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            var lista = estado switch
            {
                AppState.MenuOpciones => botonesOpciones,
                AppState.MenuJugar => botonesJugar,
                AppState.MenuPersonajes => botonesPersonajes,
                AppState.MenuMultiplayer => botonesMultiplayer,
                _ => botonesMenu
            };

            if (estado == AppState.MenuPersonajes)
            {
                if (font != null)
                {
                    string titulo = "Selecciona tu personaje";
                    var size = font.MeasureString(titulo);
                    spriteBatch.DrawString(font, titulo, new Vector2((W - size.X) * 0.5f, pos.Y + 16), Color.White);
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
                return;
            }

            // otros menús (incluye principal / opciones / jugar / multiplayer)
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
        }

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
            spriteBatch.Draw(pixel, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0,0,0,70));
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
    }
}