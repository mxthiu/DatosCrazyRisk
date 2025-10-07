#nullable enable
using System;
using CrazyRisk.Core;

namespace CrazyRiskGame.Play.Controllers
{
    /// <summary>
    /// Controla la fase de Fortify: selección de origen/destino y movimiento de tropas.
    /// No dibuja ni lee input crudo; recibe eventos de alto nivel desde la UI.
    /// </summary>
    public sealed class FortifyController
    {
        private readonly GameEngine _engine;

        /// <summary>Territorio de origen (propio) seleccionado para mover tropas.</summary>
        public string? FromId { get; private set; }

        /// <summary>Territorio de destino (propio y conectado) seleccionado.</summary>
        public string? ToId { get; private set; }

        /// <summary>Cantidad a mover por defecto (controlado con +/- en la UI).</summary>
        public int Amount { get; private set; } = 1;

        public FortifyController(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// La UI avisa que el jugador clickeó un territorio durante la fase Fortify.
        /// Primer click: fija origen si es propio y tiene al menos 2 tropas.
        /// Segundo click: fija destino si es propio y conectado por camino aliado.
        /// Un tercer click en cualquier territorio propio “reinicia” y elige nuevo origen.
        /// </summary>
        public void HandleTerritoryClick(string territoryId, out string uiMessage)
        {
            uiMessage = string.Empty;

            if (_engine.State.Phase != Phase.Fortify)
            {
                uiMessage = "No estás en fase de fortificación.";
                return;
            }

            if (!_engine.State.Territories.TryGetValue(territoryId, out var t))
            {
                uiMessage = "Territorio inválido.";
                return;
            }

            if (t.OwnerId != _engine.State.CurrentPlayerId)
            {
                uiMessage = "Debes elegir territorios propios.";
                return;
            }

            // Si no hay origen, elegimos origen válido (≥2 tropas para poder mover al menos 1)
            if (FromId is null)
            {
                if (t.Troops < 2)
                {
                    uiMessage = "El origen debe tener al menos 2 tropas.";
                    return;
                }

                FromId = territoryId;
                ToId = null;
                ClampAmountToAvailable();
                uiMessage = $"Origen: {FromId}. Ahora elige un destino propio conectado.";
                return;
            }

            // Si tenemos origen pero no destino, intentamos fijar destino
            if (ToId is null)
            {
                // Si clickea el mismo origen, limpiamos y volvemos a elegir
                if (territoryId == FromId)
                {
                    FromId = null;
                    ToId = null;
                    uiMessage = "Origen deseleccionado. Elige un nuevo origen.";
                    return;
                }

                // Debe estar conectado con camino aliado
                if (!_engine.AreConnectedByOwnerPath(FromId, territoryId, _engine.State.CurrentPlayerId))
                {
                    uiMessage = "No hay camino propio entre origen y destino.";
                    return;
                }

                ToId = territoryId;
                ClampAmountToAvailable();
                uiMessage = $"Destino: {ToId}. Usa (+/-) para ajustar y 'Confirmar' para mover.";
                return;
            }

            // Si había origen y destino y clickea de nuevo, reinicia seleccionando nuevo origen
            FromId = territoryId;
            ToId = null;
            if (_engine.State.Territories.TryGetValue(territoryId, out var nt) && nt.Troops >= 2)
            {
                ClampAmountToAvailable();
                uiMessage = $"Nuevo origen: {FromId}. Elige destino propio conectado.";
            }
            else
            {
                FromId = null;
                uiMessage = "El nuevo origen debe tener al menos 2 tropas.";
            }
        }

        /// <summary>
        /// Ajusta el Amount con +/- limitado a lo que se puede mover (disponible en origen).
        /// </summary>
        public void BumpAmount(int delta)
        {
            Amount = Math.Clamp(Amount + delta, 1, 999);
            ClampAmountToAvailable();
        }

        /// <summary>
        /// Intenta ejecutar el movimiento de tropas.
        /// </summary>
        public bool TryMove(out string uiMessage)
        {
            uiMessage = string.Empty;

            if (_engine.State.Phase != Phase.Fortify)
            {
                uiMessage = "No estás en fase de fortificación.";
                return false;
            }
            if (FromId is null) { uiMessage = "Falta elegir origen."; return false; }
            if (ToId is null)   { uiMessage = "Falta elegir destino."; return false; }

            // No permitir dejar el origen en 0 tropas
            if (!_engine.State.Territories.TryGetValue(FromId, out var fromT))
            {
                uiMessage = "Origen inválido.";
                return false;
            }

            int maxMovible = Math.Max(0, fromT.Troops - 1);
            if (maxMovible <= 0)
            {
                uiMessage = "No hay tropas movibles (debes dejar al menos 1 en origen).";
                return false;
            }

            int amount = Math.Clamp(Amount, 1, maxMovible);

            if (_engine.FortifyMove(FromId, ToId, amount, out string err))
            {
                uiMessage = $"Movimiento: {FromId} → {ToId} (+{amount}).";
                // Después de mover, muchas reglas permiten un solo movimiento por turno.
                // No limpiamos automáticamente por si la UI quiere mostrar el resultado hasta que el jugador pulse Siguiente.
                ClampAmountToAvailable();
                return true;
            }

            uiMessage = "No se pudo mover: " + err;
            return false;
        }

        /// <summary>
        /// Limpia selección (por ejemplo al pulsar “Cancelar” en la UI).
        /// </summary>
        public void ClearSelection()
        {
            FromId = null;
            ToId = null;
            Amount = 1;
        }

        /// <summary>
        /// Expone un snapshot para pintar la UI.
        /// </summary>
        public FortifyView GetView()
        {
            int fromTroops = 0;
            int toTroops = 0;
            bool pathOk = false;

            if (FromId != null && _engine.State.Territories.TryGetValue(FromId, out var f))
                fromTroops = f.Troops;

            if (ToId != null && _engine.State.Territories.TryGetValue(ToId, out var t))
                toTroops = t.Troops;

            if (FromId != null && ToId != null)
                pathOk = _engine.AreConnectedByOwnerPath(FromId, ToId, _engine.State.CurrentPlayerId);

            int maxMovible = Math.Max(0, fromTroops - 1);

            return new FortifyView(
                currentPlayerId: _engine.State.CurrentPlayerId,
                fromId: FromId,
                toId: ToId,
                fromTroops: fromTroops,
                toTroops: toTroops,
                maxMovableFrom: maxMovible,
                amount: Math.Clamp(Amount, 1, Math.Max(1, maxMovible)),
                pathOk: pathOk
            );
        }

        private void ClampAmountToAvailable()
        {
            if (FromId != null && _engine.State.Territories.TryGetValue(FromId, out var f))
            {
                int maxMovible = Math.Max(0, f.Troops - 1);
                Amount = Math.Clamp(Amount, 1, Math.Max(1, maxMovible));
            }
            else
            {
                Amount = 1;
            }
        }
    }

    /// <summary>
    /// DTO de sólo lectura para la UI de fortificación.
    /// </summary>
    public readonly struct FortifyView
    {
        public int CurrentPlayerId { get; }
        public string? FromId { get; }
        public string? ToId { get; }
        public int FromTroops { get; }
        public int ToTroops { get; }
        public int MaxMovableFrom { get; }
        public int Amount { get; }
        public bool PathOk { get; }

        public FortifyView(int currentPlayerId, string? fromId, string? toId, int fromTroops, int toTroops, int maxMovableFrom, int amount, bool pathOk)
        {
            CurrentPlayerId = currentPlayerId;
            FromId = fromId;
            ToId = toId;
            FromTroops = fromTroops;
            ToTroops = toTroops;
            MaxMovableFrom = maxMovableFrom;
            Amount = amount;
            PathOk = pathOk;
        }
    }
}
