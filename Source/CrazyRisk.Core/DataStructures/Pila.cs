using System;

namespace CrazyRisk.Core.DataStructures
{
    /// <summary>
    /// Pila (LIFO) para funcionalidad de Undo en refuerzos.
    /// </summary>
    public class Pila<T>
    {
        private T[] datos;
        private int tope;

        public Pila(int capacidad = 8)
        {
            if (capacidad < 4) capacidad = 4;
            datos = new T[capacidad];
            tope = 0;
        }

        public int Count => tope;

        public void Apilar(T elemento)
        {
            if (tope == datos.Length)
            {
                var nuevo = new T[datos.Length * 2];
                for (int i = 0; i < tope; i++)
                    nuevo[i] = datos[i];
                datos = nuevo;
            }
            datos[tope++] = elemento;
        }

        public T Desapilar()
        {
            if (tope == 0)
                throw new InvalidOperationException("Pila vacía");
            
            var valor = datos[--tope];
            datos[tope] = default!;
            return valor;
        }

        public T Peek()
        {
            if (tope == 0)
                throw new InvalidOperationException("Pila vacía");
            return datos[tope - 1];
        }

        public void Limpiar()
        {
            for (int i = 0; i < tope; i++)
                datos[i] = default!;
            tope = 0;
        }
    }
}
