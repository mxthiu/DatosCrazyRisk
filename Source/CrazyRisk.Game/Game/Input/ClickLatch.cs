using Microsoft.Xna.Framework;            // Rectangle, Point
using Microsoft.Xna.Framework.Input;      // MouseState, ButtonState

namespace CrazyRiskGame.Game.Input
{
    /// <summary>
    /// Latch para clicks: detecta flancos (just pressed) y "click dentro de rect".
    /// </summary>
    public sealed class ClickLatch
    {
        private ButtonState _prevLeft  = ButtonState.Released;
        private ButtonState _prevRight = ButtonState.Released;
        private MouseState _prevMouse;

        private MouseState _curMouse;

        public void Update(MouseState mouse)
        {
            _prevMouse  = _curMouse;
            _curMouse   = mouse;

            _prevLeft   = _prevMouse.LeftButton;
            _prevRight  = _prevMouse.RightButton;
        }

        /// <summary>¿Se presionó el botón izquierdo este frame?</summary>
        public bool LeftJustPressed()
            => _prevLeft == ButtonState.Released && _curMouse.LeftButton == ButtonState.Pressed;

        /// <summary>¿Click izquierdo dentro del rect dado (con flanco)?</summary>
        public bool LeftClickOn(Rectangle r)
            => LeftJustPressed() && r.Contains(_curMouse.Position);
    }
}
