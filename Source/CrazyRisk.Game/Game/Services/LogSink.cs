using System;
using CrazyRiskGame.Game.Abstractions;

namespace CrazyRiskGame.Game.Services
{
    /// <summary>Implementación simple de ILogSink. En tu Juego.cs probablemente inyectes un sink a uiLog.</summary>
    public sealed class LogSink : ILogSink
    {
        private readonly Action<string> _push;

        public LogSink(Action<string> pushLine)
        {
            _push = pushLine ?? (_ => { });
        }

        public void Info(string message)  => _push(message);
        public void Warn(string message)  => _push("[WARN] " + message);
        public void Error(string message) => _push("[ERROR] " + message);
    }
}
