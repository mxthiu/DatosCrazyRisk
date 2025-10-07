#nullable enable
using Microsoft.Xna.Framework.Input;

namespace CrazyRiskGame.Play.Adapters
{
    /// <summary>
    /// Adaptador de entrada muy simple para centralizar lecturas de teclado y mouse.
    /// Agrega Capture() porque Juego.cs lo llama.
    /// </summary>
    public sealed class InputAdapter
    {
        /// <summary>
        /// Devuelve el estado actual de mouse y teclado en una sola llamada.
        /// Mantén la forma exacta que usa Juego.cs.
        /// </summary>
        public (MouseState mouse, KeyboardState keyboard) Capture()
            => (Mouse.GetState(), Keyboard.GetState());

        // Si ya tenías otros métodos/propiedades, puedes dejarlos aquí.
        // Este archivo mínimo asegura que Compile con el uso actual en Juego.cs.
    }
}
