using System;

namespace CrazyRiskGame
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            using var juego = new Juego();
            juego.Run();
        }
    }
}
