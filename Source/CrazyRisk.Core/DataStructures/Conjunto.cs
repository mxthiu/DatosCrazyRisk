using System;

namespace CrazyRisk.Core.DataStructures
{
    /// <summary>
    /// Conjunto (HashSet) para marcar elementos visitados en BFS.
    /// </summary>
    public class Conjunto<T>
    {
        private bool[] usado;
        private T[] elementos;
        private int count;

        public Conjunto(int capacidad = 16)
        {
            if (capacidad < 4) capacidad = 4;
            usado = new bool[capacidad];
            elementos = new T[capacidad];
            count = 0;
        }

        public int Count => count;

        public bool Agregar(T elemento)
        {
            if (elemento == null)
                throw new ArgumentNullException(nameof(elemento));

            int hash = ObtenerHash(elemento);
            int inicio = hash;

            while (usado[hash])
            {
                if (elementos[hash]?.Equals(elemento) == true)
                    return false; // Ya existe

                hash = (hash + 1) % elementos.Length;
                if (hash == inicio)
                {
                    Redimensionar();
                    hash = ObtenerHash(elemento);
                    inicio = hash;
                }
            }

            elementos[hash] = elemento;
            usado[hash] = true;
            count++;

            if (count > elementos.Length * 0.75)
                Redimensionar();

            return true;
        }

        public bool Contiene(T elemento)
        {
            if (elemento == null)
                return false;

            int hash = ObtenerHash(elemento);
            int inicio = hash;

            while (usado[hash])
            {
                if (elementos[hash]?.Equals(elemento) == true)
                    return true;

                hash = (hash + 1) % elementos.Length;
                if (hash == inicio) break;
            }

            return false;
        }

        public void Remover(T elemento)
        {
            if (elemento == null)
                return;

            int hash = ObtenerHash(elemento);
            int inicio = hash;

            while (usado[hash])
            {
                if (elementos[hash]?.Equals(elemento) == true)
                {
                    usado[hash] = false;
                    elementos[hash] = default!;
                    count--;
                    return;
                }

                hash = (hash + 1) % elementos.Length;
                if (hash == inicio) break;
            }
        }

        public void Limpiar()
        {
            for (int i = 0; i < elementos.Length; i++)
            {
                usado[i] = false;
                elementos[i] = default!;
            }
            count = 0;
        }

        private int ObtenerHash(T elemento)
        {
            return Math.Abs(elemento.GetHashCode() % elementos.Length);
        }

        private void Redimensionar()
        {
            var viejosElementos = elementos;
            var viejosUsado = usado;

            elementos = new T[elementos.Length * 2];
            usado = new bool[elementos.Length];
            count = 0;

            for (int i = 0; i < viejosElementos.Length; i++)
            {
                if (viejosUsado[i])
                    Agregar(viejosElementos[i]);
            }
        }
    }
}
