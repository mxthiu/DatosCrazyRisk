#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace CrazyRiskGame.Play.Animations
{
    /// <summary>
    /// Animador simple de dados. Se usa desde Juego.cs para mezclar caras durante un tiempo
    /// y luego dejar visibles los valores finales que provee la lógica de combate.
    /// </summary>
    public sealed class DiceAnimator
    {
        private readonly Random _rng = new();
        private double _timer;
        private double _durationSec = 0.8;

        /// <summary>Indica si la animación está mezclando caras en este momento.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Caras mostradas para el atacante (hasta 3). Valor 0 = dado oculto/no usado.</summary>
        public int[] ShownAttacker { get; } = new int[3];

        /// <summary>Caras mostradas para el defensor (hasta 2). Valor 0 = dado oculto/no usado.</summary>
        public int[] ShownDefender { get; } = new int[2];

        /// <summary>
        /// Inicia la animación. Puedes indicar cuántos dados usa cada lado (1..3 atacante, 1..2 defensor).
        /// </summary>
        public void Start(int attackerDice = 3, int defenderDice = 2, double seconds = 0.8)
        {
            _durationSec = seconds > 0 ? seconds : 0.8;
            _timer = 0;
            IsActive = true;

            // Inicializa los slots activos (los inactivos quedan en 0 para que la UI pueda ignorarlos).
            for (int i = 0; i < ShownAttacker.Length; i++)
                ShownAttacker[i] = i < attackerDice ? _rng.Next(1, 7) : 0;

            for (int i = 0; i < ShownDefender.Length; i++)
                ShownDefender[i] = i < defenderDice ? _rng.Next(1, 7) : 0;
        }

        /// <summary>
        /// Actualiza la animación usando GameTime (firma compatible con Juego.cs).
        /// </summary>
        public void Update(GameTime gameTime)
        {
            if (!IsActive) return;

            _timer += gameTime.ElapsedGameTime.TotalSeconds;

            if (_timer < _durationSec)
            {
                // Barajar caras mientras dura la animación.
                for (int i = 0; i < ShownAttacker.Length; i++)
                    if (ShownAttacker[i] > 0) ShownAttacker[i] = _rng.Next(1, 7);

                for (int i = 0; i < ShownDefender.Length; i++)
                    if (ShownDefender[i] > 0) ShownDefender[i] = _rng.Next(1, 7);
            }
            else
            {
                // Queda a la espera de StopAndShow() para fijar resultado final.
                IsActive = false;
            }
        }

        /// <summary>
        /// Fija los valores finales de los dados y termina la animación.
        /// </summary>
        public void StopAndShow(int[] finalAttacker, int[] finalDefender)
        {
            for (int i = 0; i < ShownAttacker.Length; i++)
                ShownAttacker[i] = i < finalAttacker.Length ? Math.Clamp(finalAttacker[i], 1, 6) : 0;

            for (int i = 0; i < ShownDefender.Length; i++)
                ShownDefender[i] = i < finalDefender.Length ? Math.Clamp(finalDefender[i], 1, 6) : 0;

            IsActive = false;
            _timer = _durationSec;
        }
    }
}
