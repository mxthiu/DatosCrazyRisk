#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CrazyRiskGame.Play.UI
{
    /// <summary>
    /// Paleta centralizada para mapear ownerId -> Color.
    /// - Trae colores por defecto para 0(Azul), 1(Rojo), 2(Verde).
    /// - Para IDs que no estén configurados, cicla una lista de fallback.
    /// - Permite override por código (SetColor) y reset a defaults (ResetDefaults).
    /// </summary>
    public static class OwnerColorPalette
    {
        // Colores por defecto para los primeros jugadores
        // (escogidos para buen contraste sobre las máscaras y el mapa)
        private static readonly Dictionary<int, Color> _defaults = new()
        {
            { 0, new Color( 80, 150, 255) }, // Azul
            { 1, new Color(220,  60,  60) }, // Rojo
            { 2, new Color( 60, 190,  80) }, // Verde
        };

        // Fallbacks si aparecen más IDs de jugadores
        private static readonly Color[] _fallbackCycle = new[]
        {
            new Color(250, 180,  60), // Naranja
            new Color(160, 100, 240), // Morado
            new Color( 70, 220, 210), // Cian
            new Color(240, 240,  80), // Amarillo
            new Color(255, 110, 180), // Rosa
            new Color(120, 120, 120), // Gris
        };

        // Overrides en runtime (prioridad sobre defaults)
        private static readonly Dictionary<int, Color> _overrides = new();

        /// <summary>
        /// Obtiene el color asignado al ownerId.
        /// Si no hay override ni default, usa un color del ciclo de fallback.
        /// </summary>
        public static Color GetColor(int ownerId)
        {
            if (_overrides.TryGetValue(ownerId, out var c)) return c;
            if (_defaults.TryGetValue(ownerId, out c)) return c;

            // Ciclo determinista por ID para consistencia visual.
            int idx = Math.Abs(ownerId) % _fallbackCycle.Length;
            return _fallbackCycle[idx];
        }

        /// <summary>
        /// Intenta obtener un color explícito (override o default).
        /// No recurre al ciclo de fallback.
        /// </summary>
        public static bool TryGetExplicitColor(int ownerId, out Color color)
        {
            if (_overrides.TryGetValue(ownerId, out color)) return true;
            if (_defaults.TryGetValue(ownerId, out color)) return true;
            color = default;
            return false;
        }

        /// <summary>
        /// Define o reemplaza el color para un ownerId específico (override).
        /// </summary>
        public static void SetColor(int ownerId, Color color)
        {
            _overrides[ownerId] = color;
        }

        /// <summary>
        /// Quita un override (si existía) para el ownerId.
        /// </summary>
        public static void ClearOverride(int ownerId)
        {
            _overrides.Remove(ownerId);
        }

        /// <summary>
        /// Limpia todos los overrides y vuelve a defaults.
        /// </summary>
        public static void ResetDefaults()
        {
            _overrides.Clear();
        }
    }
}
