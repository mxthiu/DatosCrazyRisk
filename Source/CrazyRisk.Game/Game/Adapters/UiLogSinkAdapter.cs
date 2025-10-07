// Source/CrazyRisk.Game/Game/Adapters/UiLogSinkAdapter.cs
using System;
using CrazyRiskGame.Game.Abstractions;

namespace CrazyRiskGame.Game.Adapters
{
    /// <summary>
    /// Adaptador que envía los logs a un Action<string> (p.ej. uiLog.Add).
    /// </summary>
    public sealed class UiLogSinkAdapter : ILogSink
    {
        private readonly Action<string> sink;

        public UiLogSinkAdapter(Action<string> sink)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Info(string message)  => sink(message);
        public void Warn(string message)  => sink("⚠️ " + message);
        public void Error(string message) => sink("❌ " + message);
    }
}
