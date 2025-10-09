using System;

namespace CrazyRisk.Core.DataStructures
{
    /// <summary>
    /// Diccionario con hash table y sondeo lineal.
    /// </summary>
    public class Diccionario<TKey, TValue>
    {
        private struct Entry
        {
            public TKey Key;
            public TValue Value;
            public bool Used;
        }

        private Entry[] buckets;
        private int count;

        public Diccionario(int capacidad = 16)
        {
            if (capacidad < 4) capacidad = 4;
            buckets = new Entry[capacidad];
            count = 0;
        }

        public int Count => count;

        public TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out var value))
                    return value;
                throw new KeyNotFoundException($"Clave no encontrada: {key}");
            }
            set
            {
                int hash = ObtenerHash(key);
                int inicio = hash;

                while (buckets[hash].Used)
                {
                    if (buckets[hash].Key?.Equals(key) == true)
                    {
                        buckets[hash].Value = value;
                        return;
                    }
                    hash = (hash + 1) % buckets.Length;
                    if (hash == inicio)
                    {
                        Redimensionar();
                        hash = ObtenerHash(key);
                        inicio = hash;
                    }
                }

                buckets[hash] = new Entry { Key = key, Value = value, Used = true };
                count++;

                if (count > buckets.Length * 0.75)
                    Redimensionar();
            }
        }

        public void Agregar(TKey key, TValue value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hash = ObtenerHash(key);
            int inicio = hash;

            while (buckets[hash].Used)
            {
                if (buckets[hash].Key?.Equals(key) == true)
                    throw new InvalidOperationException("Clave duplicada");
                
                hash = (hash + 1) % buckets.Length;
                if (hash == inicio)
                {
                    Redimensionar();
                    hash = ObtenerHash(key);
                    inicio = hash;
                }
            }

            buckets[hash] = new Entry { Key = key, Value = value, Used = true };
            count++;

            if (count > buckets.Length * 0.75)
                Redimensionar();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key == null)
            {
                value = default!;
                return false;
            }

            int hash = ObtenerHash(key);
            int inicio = hash;

            while (buckets[hash].Used)
            {
                if (buckets[hash].Key?.Equals(key) == true)
                {
                    value = buckets[hash].Value;
                    return true;
                }
                hash = (hash + 1) % buckets.Length;
                if (hash == inicio) break;
            }

            value = default!;
            return false;
        }

        public bool ContieneKey(TKey key)
        {
            return TryGetValue(key, out _);
        }

        public void Remover(TKey key)
        {
            if (key == null) return;

            int hash = ObtenerHash(key);
            int inicio = hash;

            while (buckets[hash].Used)
            {
                if (buckets[hash].Key?.Equals(key) == true)
                {
                    buckets[hash].Used = false;
                    buckets[hash].Key = default!;
                    buckets[hash].Value = default!;
                    count--;
                    return;
                }
                hash = (hash + 1) % buckets.Length;
                if (hash == inicio) break;
            }
        }

        public void Limpiar()
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = default;
            }
            count = 0;
        }

        public void ParaCada(Action<TKey, TValue> accion)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].Used)
                    accion(buckets[i].Key, buckets[i].Value);
            }
        }

        private int ObtenerHash(TKey key)
        {
            return Math.Abs(key.GetHashCode() % buckets.Length);
        }

        private void Redimensionar()
        {
            var viejos = buckets;
            buckets = new Entry[viejos.Length * 2];
            count = 0;

            for (int i = 0; i < viejos.Length; i++)
            {
                if (viejos[i].Used)
                    Agregar(viejos[i].Key, viejos[i].Value);
            }
        }
    }

    public class KeyNotFoundException : Exception
    {
        public KeyNotFoundException(string message) : base(message) { }
    }
}
