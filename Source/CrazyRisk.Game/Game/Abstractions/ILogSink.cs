namespace CrazyRiskGame.Game.Abstractions
{
    /// <summary>Salida de log simple para inyectar en acciones/UI.</summary>
    public interface ILogSink
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }
}
