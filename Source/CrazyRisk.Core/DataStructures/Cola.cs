using System;

namespace CrazyRisk.Core.DataStructures
{
    /// <summary>
    /// Cola circular (FIFO) para algoritmos BFS.
    /// </summary>
    public class Cola<T>
    {
        private T[] datos;
        private int inicio, fin, count;

        public Cola(int capacidad = 8)
        {
            if (capacidad < 4) capacidad = 4;
            datos = new T[capacidad];
            inicio = 0;
            fin = 0;
            count = 0;
        }

        public int Count => count;

        public void Encolar(T elemento)
        {
            if (count == datos.Length)
                Redimensionar();

            datos[fin] = elemento;
            fin = (fin + 1) % datos.Length;
            count++;
        }

        public T Desencolar()
        {
            if (count == 0)
                throw new InvalidOperationException("Cola vacía");

            var valor = datos[inicio];
            datos[inicio] = default!;
            inicio = (inicio + 1) % datos.Length;
            count--;
            return valor;
        }

        public T Peek()
        {
            if (count == 0)
                throw new InvalidOperationException("Cola vacía");
            return datos[inicio];
        }

        public void Limpiar()
        {
            for (int i = 0; i < count; i++)
            {
                int idx = (inicio + i) % datos.Length;
                datos[idx] = default!;
            }
            inicio = 0;
            fin = 0;
            count = 0;
        }

        private void Redimensionar()
        {
            var nuevo = new T[datos.Length * 2];
            for (int i = 0; i < count; i++)
                nuevo[i] = datos[(inicio + i) % datos.Length];
            
            datos = nuevo;
            inicio = 0;
            fin = count;
        }
    }
}
