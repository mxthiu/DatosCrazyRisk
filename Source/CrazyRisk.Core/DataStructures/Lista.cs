using System;

namespace CrazyRisk.Core.DataStructures
{
    /// <summary>
    /// Lista dinámica genérica con redimensionamiento automático.
    /// </summary>
    public class Lista<T>
    {
        private T[] datos;
        private int count;

        public Lista(int capacidadInicial = 4)
        {
            if (capacidadInicial < 1) capacidadInicial = 4;
            datos = new T[capacidadInicial];
            count = 0;
        }

        public int Count => count;

        public T this[int indice]
        {
            get
            {
                if (indice < 0 || indice >= count)
                    throw new IndexOutOfRangeException();
                return datos[indice];
            }
            set
            {
                if (indice < 0 || indice >= count)
                    throw new IndexOutOfRangeException();
                datos[indice] = value;
            }
        }

        public void Agregar(T elemento)
        {
            if (count == datos.Length)
                Redimensionar();
            datos[count++] = elemento;
        }

        public void Remover(T elemento)
        {
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                if (datos[i]?.Equals(elemento) == true)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                RemoverEn(index);
        }

        public void RemoverEn(int indice)
        {
            if (indice < 0 || indice >= count)
                throw new IndexOutOfRangeException();

            for (int i = indice; i < count - 1; i++)
                datos[i] = datos[i + 1];

            datos[--count] = default!;
        }

        public void Limpiar()
        {
            for (int i = 0; i < count; i++)
                datos[i] = default!;
            count = 0;
        }

        public bool Contiene(T elemento)
        {
            for (int i = 0; i < count; i++)
                if (datos[i]?.Equals(elemento) == true)
                    return true;
            return false;
        }

        public T? Buscar(Func<T, bool> predicado)
        {
            for (int i = 0; i < count; i++)
                if (predicado(datos[i]))
                    return datos[i];
            return default;
        }

        public void ParaCada(Action<T> accion)
        {
            for (int i = 0; i < count; i++)
                accion(datos[i]);
        }

        public T[] ToArray()
        {
            var resultado = new T[count];
            for (int i = 0; i < count; i++)
                resultado[i] = datos[i];
            return resultado;
        }

        private void Redimensionar()
        {
            var nuevo = new T[datos.Length * 2];
            for (int i = 0; i < count; i++)
                nuevo[i] = datos[i];
            datos = nuevo;
        }
    }
}
