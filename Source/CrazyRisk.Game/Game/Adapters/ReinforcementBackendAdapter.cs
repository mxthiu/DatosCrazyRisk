using System;
using System.Reflection;
using CrazyRisk.Core;
using CrazyRiskGame.Game.Abstractions;

namespace CrazyRiskGame.Game.Adapters
{
    /// <summary>
    /// Adaptador robusto: NO referencia firmas concretas en compile-time.
    /// En runtime detecta e invoca el método que exista en el controller:
    /// - bool TryPlace(out string err, string territoryId)
    /// - bool TryPlace(string territoryId)
    /// - void PlaceOne(string territoryId)
    /// </summary>
    public sealed class ReinforcementBackendAdapter : IReinforcementBackend
    {
        private readonly GameEngine _engine;
        private readonly object _controller;
        private readonly ILogSink _log;

        public ReinforcementBackendAdapter(GameEngine engine, object reinforcementController, ILogSink log)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _controller = reinforcementController ?? throw new ArgumentNullException(nameof(reinforcementController));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public int ReinforcementsRemaining => _engine.State.ReinforcementsRemaining;

        public void PlaceOne(string territoryId)
        {
            if (string.IsNullOrWhiteSpace(territoryId))
            {
                _log.Warn("Reinforcements: territoryId vacío.");
                return;
            }

            // 1) TryPlace(out string err, string territoryId)
            var ctlType = _controller.GetType();
            var tryPlaceOutSig = ctlType.GetMethod(
                "TryPlace",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string).MakeByRefType(), typeof(string) },
                modifiers: null
            );

            if (tryPlaceOutSig != null)
            {
                object?[] args = new object?[] { null, territoryId };
                var ok = (bool)tryPlaceOutSig.Invoke(_controller, args)!;
                if (!ok)
                {
                    var err = args[0] as string ?? "desconocido";
                    _log.Warn($"Refuerzos: no se pudo colocar en {territoryId}: {err}");
                    return;
                }
                _log.Info($"Refuerzos: colocado en {territoryId}.");
                return;
            }

            // 2) TryPlace(string territoryId)
            var tryPlace1Sig = ctlType.GetMethod(
                "TryPlace",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            );

            if (tryPlace1Sig != null)
            {
                var ok = (bool)tryPlace1Sig.Invoke(_controller, new object[] { territoryId })!;
                if (!ok)
                {
                    _log.Warn($"Refuerzos: no se pudo colocar en {territoryId}.");
                    return;
                }
                _log.Info($"Refuerzos: colocado en {territoryId}.");
                return;
            }

            // 3) PlaceOne(string territoryId) (por si tu controller ya trae un método directo)
            var placeOneSig = ctlType.GetMethod(
                "PlaceOne",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            );

            if (placeOneSig != null)
            {
                placeOneSig.Invoke(_controller, new object[] { territoryId });
                _log.Info($"Refuerzos: colocado en {territoryId}.");
                return;
            }

            // 4) Fallback (opcional): si no hay ninguno, avisa claramente
            _log.Error("Reinforcements: el controller no expone TryPlace/PlaceOne compatibles.");
        }
    }
}
