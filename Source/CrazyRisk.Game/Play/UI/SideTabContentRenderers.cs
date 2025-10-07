#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Tipo de “intención” que un tab quiere ejecutar (lo manejará Juego.cs llamando al GameEngine).
    /// </summary>
    public enum SideTabActionType
    {
        None = 0,

        // Refuerzos:
        RequestPlaceReinforcements,   // Amount, TerritoryId (opcional si lo toma del seleccionado)
        ChangeReinforcementStep,      // Amount = +/- delta

        // Ataque:
        RequestSetAttackFrom,         // TerritoryId
        RequestSetAttackTo,           // TerritoryId
        RequestRollAttack,            // sin args

        // Movimiento:
        RequestFortifyMove,           // TerritoryId, TerritoryId2, Amount

        // Cartas:
        RequestOpenCardsExchange      // sin args (abrir/canjear)
    }

    /// <summary>
    /// Mensaje de salida de los tabs. Juego.cs leerá esto y actuará.
    /// </summary>
    public struct SideTabAction
    {
        public SideTabActionType Type;
        public int Amount;              // genérico: refuerzos a colocar / tropas a mover / delta step
        public string? TerritoryId;     // origen o seleccionado
        public string? TerritoryId2;    // destino (fortify)
        public static readonly SideTabAction None = new() { Type = SideTabActionType.None };
    }

    /// <summary>
    /// Utilidades de UI (botones, rectángulos, textos) para estos tabs.
    /// </summary>
    internal static class UIPrimitives
    {
        public static void DrawRect(SpriteBatch sb, Texture2D px, Rectangle r, Color c, int thickness = 1)
        {
            sb.Draw(px, new Rectangle(r.Left, r.Top, r.Width, thickness), c);
            sb.Draw(px, new Rectangle(r.Left, r.Bottom - thickness, r.Width, thickness), c);
            sb.Draw(px, new Rectangle(r.Left, r.Top, thickness, r.Height), c);
            sb.Draw(px, new Rectangle(r.Right - thickness, r.Top, thickness, r.Height), c);
        }

        public static void DrawPrimaryButton(SpriteBatch sb, Texture2D px, SpriteFont font, Rectangle r, string text, bool hover)
        {
            var bgA = hover ? new Color(255, 255, 255, 220) : new Color(255, 255, 255, 200);
            sb.Draw(px, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0, 0, 0, 70));
            sb.Draw(px, r, bgA);
            DrawRect(sb, px, r, new Color(0, 0, 0, 190), 2);

            var s = font.MeasureString(text);
            sb.DrawString(font, text,
                new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f),
                Color.Black);
        }

        public static void DrawFlatButton(SpriteBatch sb, Texture2D px, SpriteFont font, Rectangle r, string text, bool hover)
        {
            sb.Draw(px, r, hover ? new Color(255, 255, 255, 160) : new Color(255, 255, 255, 130));
            DrawRect(sb, px, r, new Color(0, 0, 0, 180), 1);
            var s = font.MeasureString(text);
            sb.DrawString(font, text,
                new Vector2(r.X + (r.Width - s.X) * 0.5f, r.Y + (r.Height - s.Y) * 0.5f),
                Color.Black);
        }
    }

    /// <summary>
    /// Render y manejo de la pestaña "Refuerzos".
    /// Dependencias externas que debe pasar Juego.cs:
    ///  - refuerzosPendientes
    ///  - step (por ref) para +/- de la cantidad por click
    ///  - territorioSeleccionado (opcional; si existe, botón “colocar en seleccionado” usa ese id)
    /// </summary>
    public sealed class ReinforcementsTabRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;

        // cachés de hover para botones simples del panel
        private Rectangle _btnMinus, _btnPlus, _btnPlace;
        private bool _hMinus, _hPlus, _hPlace;

        public ReinforcementsTabRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _px = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        public void Draw(SpriteBatch sb, Rectangle contentArea, int refuerzosPendientes, int step, string? territorioSeleccionado)
        {
            UIPrimitives.DrawRect(sb, _px, contentArea, new Color(255, 255, 255, 60), 1);

            sb.DrawString(_font, "Refuerzos", new Vector2(contentArea.X + 6, contentArea.Y + 6), Color.White);

            // Bloque info
            var r1 = new Rectangle(contentArea.X + 6, contentArea.Y + 32, contentArea.Width - 12, 40);
            sb.Draw(_px, r1, new Color(255, 255, 255, 20));
            UIPrimitives.DrawRect(sb, _px, r1, new Color(255, 255, 255, 60), 1);
            sb.DrawString(_font, $"Pendientes: {refuerzosPendientes}", new Vector2(r1.X + 8, r1.Y + 10), Color.White);

            // Step +/- y box
            _btnMinus = new Rectangle(r1.Right - 160, r1.Y + 6, 36, 28);
            _btnPlus  = new Rectangle(r1.Right - 120, r1.Y + 6, 36, 28);
            var stepBox  = new Rectangle(r1.Right - 76, r1.Y + 6, 64, 28);
            UIPrimitives.DrawFlatButton(sb, _px, _font, _btnMinus, "-", _hMinus);
            UIPrimitives.DrawFlatButton(sb, _px, _font, _btnPlus , "+", _hPlus);

            sb.Draw(_px, stepBox, new Color(255, 255, 255, 100));
            UIPrimitives.DrawRect(sb, _px, stepBox, new Color(0, 0, 0, 180), 1);
            var s = _font.MeasureString(step.ToString());
            sb.DrawString(_font, step.ToString(),
                new Vector2(stepBox.X + (stepBox.Width - s.X) * 0.5f, stepBox.Y + (stepBox.Height - s.Y) * 0.5f),
                Color.Black);

            // Botón colocar en seleccionado
            _btnPlace = new Rectangle(contentArea.X + 6, r1.Bottom + 10, contentArea.Width - 12, 40);
            var label = territorioSeleccionado != null
                ? $"Colocar +{step} en {territorioSeleccionado}"
                : "Colocar en territorio seleccionado";
            UIPrimitives.DrawPrimaryButton(sb, _px, _font, _btnPlace, label, _hPlace);

            // Desc
            if (territorioSeleccionado == null)
            {
                sb.DrawString(_font, "Consejo: haz click en el mapa para elegir un territorio propio.",
                    new Vector2(contentArea.X + 6, _btnPlace.Bottom + 8), Color.White);
            }
        }

        public SideTabAction HandleInput(MouseState mouse, int refuerzosPendientes, ref int step, string? territorioSeleccionado)
        {
            var p = mouse.Position;
            _hMinus = _btnMinus.Contains(p);
            _hPlus  = _btnPlus.Contains(p);
            _hPlace = _btnPlace.Contains(p);

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                if (_hMinus) { step = Math.Max(1, step - 1); return new SideTabAction { Type = SideTabActionType.ChangeReinforcementStep, Amount = -1 }; }
                if (_hPlus)  { step = Math.Min(99, step + 1); return new SideTabAction { Type = SideTabActionType.ChangeReinforcementStep, Amount = +1 }; }
                if (_hPlace && refuerzosPendientes > 0)
                {
                    int amount = Math.Min(step, refuerzosPendientes);
                    return new SideTabAction
                    {
                        Type = SideTabActionType.RequestPlaceReinforcements,
                        Amount = amount,
                        TerritoryId = territorioSeleccionado // puede ir null y Juego.cs decide qué hacer
                    };
                }
            }
            return SideTabAction.None;
        }
    }

    /// <summary>
    /// Render y manejo de la pestaña "Ataque".
    /// Muestra atacante/defensor actuales y botón “Lanzar dados”.
    /// </summary>
    public sealed class AttackTabRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;

        private Rectangle _btnRoll;
        private bool _hRoll;

        public AttackTabRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _px = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        public void Draw(SpriteBatch sb, Rectangle contentArea, string? attackerId, string? defenderId, int[] diceShownAtt, int[] diceShownDef, Texture2D[] diceFaces)
        {
            UIPrimitives.DrawRect(sb, _px, contentArea, new Color(255, 255, 255, 60), 1);
            sb.DrawString(_font, "Ataque", new Vector2(contentArea.X + 6, contentArea.Y + 6), Color.White);

            var attBox = new Rectangle(contentArea.X + 6, contentArea.Y + 30, contentArea.Width - 12, 54);
            var defBox = new Rectangle(contentArea.X + 6, attBox.Bottom + 6, contentArea.Width - 12, 54);
            sb.Draw(_px, attBox, new Color(255,255,255,20));
            sb.Draw(_px, defBox, new Color(255,255,255,20));
            UIPrimitives.DrawRect(sb, _px, attBox, new Color(255,255,255,60), 1);
            UIPrimitives.DrawRect(sb, _px, defBox, new Color(255,255,255,60), 1);

            sb.DrawString(_font, $"Atacante: {attackerId ?? "(elige en el mapa)"}", new Vector2(attBox.X + 8, attBox.Y + 8), Color.White);
            sb.DrawString(_font, $"Defensor : {defenderId ?? "(elige adyacente)"}", new Vector2(defBox.X + 8, defBox.Y + 8), Color.White);

            _btnRoll = new Rectangle(contentArea.X + 6, defBox.Bottom + 10, contentArea.Width - 12, 40);
            UIPrimitives.DrawPrimaryButton(sb, _px, _font, _btnRoll, "Lanzar dados", _hRoll);

            // Tira de dados
            var strip = new Rectangle(contentArea.X + 6, _btnRoll.Bottom + 8, contentArea.Width - 12, 72);
            UIPrimitives.DrawRect(sb, _px, strip, new Color(255,255,255,40), 1);

            if (diceFaces.Length >= 6)
            {
                int nA = Math.Max(1, diceShownAtt.Length);
                int nD = Math.Max(1, diceShownDef.Length);
                int gap = 10;
                int dieSize = Math.Min((strip.Height - 10), (strip.Width - gap * (nA + nD + 2)) / (nA + nD));
                dieSize = Math.Max(28, dieSize);

                int x = strip.X + gap;
                int y = strip.Y + (strip.Height - dieSize) / 2;

                // Att
                for (int i = 0; i < nA; i++)
                {
                    int val = Math.Clamp(diceShownAtt[i], 1, 6);
                    var tex = diceFaces[val - 1];
                    float sc = MathF.Min((float)dieSize / tex.Width, (float)dieSize / tex.Height);
                    sb.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                    x += dieSize + gap;
                }

                // espacio
                x += gap;

                // Def
                for (int i = 0; i < nD; i++)
                {
                    int val = Math.Clamp(diceShownDef[i], 1, 6);
                    var tex = diceFaces[val - 1];
                    float sc = MathF.Min((float)dieSize / tex.Width, (float)dieSize / tex.Height);
                    sb.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
                    x += dieSize + gap;
                }
            }
        }

        public SideTabAction HandleInput(MouseState mouse, string? attackerId, string? defenderId)
        {
            var p = mouse.Position;
            _hRoll = _btnRoll.Contains(p);

            if (mouse.LeftButton == ButtonState.Pressed && _hRoll)
            {
                // Solo pedimos tirada; Juego.cs validará con GameEngine fase y selección
                return new SideTabAction { Type = SideTabActionType.RequestRollAttack };
            }

            // Nota: elegir atacante/defensor lo haces con clicks en el mapa (ya lo tienes en Juego.cs).
            return SideTabAction.None;
        }
    }

    /// <summary>
    /// Render y manejo de la pestaña "Movimiento".
    /// Muestra +/- de cantidad a mover y Juego.cs provee origen/destino por clic en mapa.
    /// </summary>
    public sealed class MovementTabRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;

        private Rectangle _btnMinus, _btnPlus, _btnMove;
        private bool _hMinus, _hPlus, _hMove;

        public MovementTabRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _px = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        public void Draw(SpriteBatch sb, Rectangle contentArea, string? origenId, string? destinoId, int amount)
        {
            UIPrimitives.DrawRect(sb, _px, contentArea, new Color(255, 255, 255, 60), 1);
            sb.DrawString(_font, "Movimiento", new Vector2(contentArea.X + 6, contentArea.Y + 6), Color.White);

            var info = new Rectangle(contentArea.X + 6, contentArea.Y + 30, contentArea.Width - 12, 48);
            sb.Draw(_px, info, new Color(255,255,255,18));
            UIPrimitives.DrawRect(sb, _px, info, new Color(255,255,255,60), 1);
            sb.DrawString(_font, $"Origen: {origenId ?? "(elige propio)"}  →  Destino: {destinoId ?? "(elige propio conectado)"}",
                new Vector2(info.X + 8, info.Y + 12), Color.White);

            // Slider pobre +/- y box cantidad
            var row = new Rectangle(contentArea.X + 6, info.Bottom + 8, contentArea.Width - 12, 40);
            sb.Draw(_px, row, new Color(255,255,255,18));
            UIPrimitives.DrawRect(sb, _px, row, new Color(255,255,255,60), 1);
            _btnMinus = new Rectangle(row.X + 8, row.Y + 6, 36, 28);
            _btnPlus  = new Rectangle(row.Right - 44, row.Y + 6, 36, 28);
            UIPrimitives.DrawFlatButton(sb, _px, _font, _btnMinus, "-", _hMinus);
            UIPrimitives.DrawFlatButton(sb, _px, _font, _btnPlus , "+", _hPlus);
            var box = new Rectangle(_btnMinus.Right + 8, row.Y + 6, row.Width - (_btnMinus.Width + _btnPlus.Width + 32), 28);
            sb.Draw(_px, box, new Color(255,255,255,80));
            UIPrimitives.DrawRect(sb, _px, box, new Color(0,0,0,180), 1);
            var s = _font.MeasureString(amount.ToString());
            sb.DrawString(_font, amount.ToString(), new Vector2(box.X + (box.Width - s.X)/2, box.Y + (box.Height - s.Y)/2), Color.Black);

            _btnMove = new Rectangle(contentArea.X + 6, row.Bottom + 10, contentArea.Width - 12, 40);
            UIPrimitives.DrawPrimaryButton(sb, _px, _font, _btnMove, "Mover tropas", _hMove);
        }

        public SideTabAction HandleInput(MouseState mouse, ref int amount, string? origenId, string? destinoId)
        {
            var p = mouse.Position;
            _hMinus = _btnMinus.Contains(p);
            _hPlus  = _btnPlus.Contains(p);
            _hMove  = _btnMove.Contains(p);

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                if (_hMinus) amount = Math.Max(1, amount - 1);
                else if (_hPlus) amount = Math.Min(99, amount + 1);
                else if (_hMove)
                {
                    return new SideTabAction
                    {
                        Type = SideTabActionType.RequestFortifyMove,
                        Amount = Math.Max(1, amount),
                        TerritoryId = origenId,
                        TerritoryId2 = destinoId
                    };
                }
            }
            return SideTabAction.None;
        }
    }

    /// <summary>
    /// Render y manejo de la pestaña "Cartas".
    /// </summary>
    public sealed class CardsTabRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;

        private Rectangle _btnExchange;
        private bool _hEx;

        public CardsTabRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _px = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        public void Draw(SpriteBatch sb, Rectangle contentArea, int handCount, int? nextFiboBonus)
        {
            UIPrimitives.DrawRect(sb, _px, contentArea, new Color(255, 255, 255, 60), 1);
            sb.DrawString(_font, "Cartas", new Vector2(contentArea.X + 6, contentArea.Y + 6), Color.White);

            var grid = new Rectangle(contentArea.X + 6, contentArea.Y + 30, contentArea.Width - 12, contentArea.Height - 90);
            sb.Draw(_px, grid, new Color(255,255,255,18));
            UIPrimitives.DrawRect(sb, _px, grid, new Color(255,255,255,60), 1);

            sb.DrawString(_font, $"En mano: {handCount}  |  Próximo bono (Fibonacci): {(nextFiboBonus.HasValue ? nextFiboBonus.Value : 0)}",
                new Vector2(grid.X + 8, grid.Y + 8), Color.White);

            _btnExchange = new Rectangle(contentArea.X + 6, contentArea.Bottom - 48, contentArea.Width - 12, 40);
            UIPrimitives.DrawPrimaryButton(sb, _px, _font, _btnExchange, "Canjear set", _hEx);
        }

        public SideTabAction HandleInput(MouseState mouse)
        {
            var p = mouse.Position;
            _hEx = _btnExchange.Contains(p);

            if (mouse.LeftButton == ButtonState.Pressed && _hEx)
            {
                return new SideTabAction { Type = SideTabActionType.RequestOpenCardsExchange };
            }
            return SideTabAction.None;
        }
    }

    /// <summary>
    /// Render de la pestaña "Log".
    /// </summary>
    public sealed class LogTabRenderer
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;

        public LogTabRenderer(SpriteFont font, Texture2D pixel)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _px = pixel ?? throw new ArgumentNullException(nameof(pixel));
        }

        public void Draw(SpriteBatch sb, Rectangle contentArea, IReadOnlyList<string> uiLog)
        {
            UIPrimitives.DrawRect(sb, _px, contentArea, new Color(255, 255, 255, 60), 1);
            sb.DrawString(_font, "Log", new Vector2(contentArea.X + 6, contentArea.Y + 6), Color.White);

            var box = new Rectangle(contentArea.X + 6, contentArea.Y + 28, contentArea.Width - 12, contentArea.Height - 34);
            sb.Draw(_px, box, new Color(255,255,255,16));
            UIPrimitives.DrawRect(sb, _px, box, new Color(255,255,255,60), 1);

            int y = box.Y + 8;
            for (int i = Math.Max(0, uiLog.Count - 100); i < uiLog.Count; i++)
            {
                var line = uiLog[i];
                sb.DrawString(_font, line, new Vector2(box.X + 8, y), Color.White);
                y += 18;
                if (y > box.Bottom - 20) break;
            }
        }
    }
}
