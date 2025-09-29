using System;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CrazyRisk
{
    // Modelos simples para leer el JSON de territorios
    public record Anchor(int x, int y);
    public record Territory(string id, string name, string continentId, string[] neighbors, Anchor anchor);
    public record TerritoriesRoot(Territory[] territories);

    public class Game1 : Microsoft.Xna.Framework.Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // Resolución virtual retro y escala de ventana
        private const int VIRTUAL_W = 320;
        private const int VIRTUAL_H = 180;
        private const int SCALE     = 6;   // 320x180 * 6 = 1920x1080

        // Lienzo y utilería
        private RenderTarget2D _canvas;
        private Texture2D _pixel;

        // Sprites opcionales
        private Texture2D _logo;
        private Texture2D _map;

        // Datos de territorios (anchors)
        private TerritoriesRoot _data;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth  = VIRTUAL_W * SCALE;
            _graphics.PreferredBackBufferHeight = VIRTUAL_H * SCALE;
            _graphics.ApplyChanges();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _canvas = new RenderTarget2D(
                GraphicsDevice, VIRTUAL_W, VIRTUAL_H,
                false, SurfaceFormat.Color, DepthFormat.None, 0,
                RenderTargetUsage.DiscardContents
            );

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            // Carga de assets desde Content (si no existen, ignoramos)
            TryLoadTexture("Sprites/logo", out _logo);
            TryLoadTexture("Sprites/map_base", out _map);

            // Lee Config/territories.json (sube 4 niveles desde bin/... hasta el repo y entra a Config)
            try
            {
                var jsonPath = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Config", "territories.json")
                );
                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    _data = JsonSerializer.Deserialize<TerritoriesRoot>(json);
                }
            }
            catch
            {
                _data = null;
            }
        }

        private void TryLoadTexture(string contentKey, out Texture2D tex)
        {
            try { tex = Content.Load<Texture2D>(contentKey); }
            catch { tex = null; }
        }

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // 1) Dibujar al lienzo virtual
            GraphicsDevice.SetRenderTarget(_canvas);
            GraphicsDevice.Clear(new Color(16, 16, 24));

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp); // pixel-perfect

            // Fondo de mapa si existe (debe ser 320x180)
            if (_map != null)
            {
                _spriteBatch.Draw(_map, new Vector2(0, 0), Color.White);
            }

            // Logo centrado con auto-resize (si el PNG es grande)
            if (_logo != null)
            {
                const int maxW = 160;
                const int maxH = 60;
                float scale = 1f;

                if (_logo.Width > maxW || _logo.Height > maxH)
                {
                    float sx = (float)maxW / _logo.Width;
                    float sy = (float)maxH / _logo.Height;
                    scale = MathF.Min(sx, sy);
                }

                int drawW = (int)(_logo.Width * scale);
                int drawH = (int)(_logo.Height * scale);
                int x = (VIRTUAL_W - drawW) / 2;
                int y = (VIRTUAL_H - drawH) / 2;

                _spriteBatch.Draw(_logo, new Rectangle(x, y, drawW, drawH), Color.White);
            }
            else
            {
                // Placeholder si no hay logo
                int logoW = 160, logoH = 40;
                int logoX = (VIRTUAL_W - logoW) / 2;
                int logoY = (VIRTUAL_H - logoH) / 2;

                _spriteBatch.Draw(_pixel, new Rectangle(logoX, logoY, logoW, 1), Color.CornflowerBlue);
                _spriteBatch.Draw(_pixel, new Rectangle(logoX, logoY + logoH - 1, logoW, 1), Color.CornflowerBlue);
                _spriteBatch.Draw(_pixel, new Rectangle(logoX, logoY, 1, logoH), Color.CornflowerBlue);
                _spriteBatch.Draw(_pixel, new Rectangle(logoX + logoW - 1, logoY, 1, logoH), Color.CornflowerBlue);
                _spriteBatch.Draw(_pixel, new Rectangle(logoX + 4, logoY + 12, logoW - 8, 16), Color.MediumPurple);
            }

            // Anchors desde JSON (puntitos morados)
            if (_data?.territories != null)
            {
                foreach (var t in _data.territories)
                {
                    _spriteBatch.Draw(_pixel, new Rectangle(t.anchor.x - 1, t.anchor.y - 1, 3, 3), Color.MediumPurple);
                }
            }

            _spriteBatch.End();

            // 2) Escalar el lienzo virtual a la ventana
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(
                _canvas,
                destinationRectangle: new Rectangle(0, 0, VIRTUAL_W * SCALE, VIRTUAL_H * SCALE),
                color: Color.White
            );
            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
