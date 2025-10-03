using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CrazyRisk.Game
{
    // ¡OJO!: Heredamos con el nombre COMPLETO para evitar el choque con el namespace "CrazyRisk.Game"
    public class Juego : Microsoft.Xna.Framework.Game
    {
        private GraphicsDeviceManager graficos;
        private SpriteBatch? spriteBatch;

        public Juego()
        {
            graficos = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            // Tamaño inicial básico; luego lo ajustaremos al tamaño del mapa.
            graficos.PreferredBackBufferWidth = 1280;
            graficos.PreferredBackBufferHeight = 720;
            graficos.ApplyChanges();
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // spriteBatch.Begin(); // sin dibujar nada todavía
            // spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
