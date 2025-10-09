# Estructuras de Datos Implementadas - CrazyRisk

## Resumen

En cumplimiento con el requerimiento de no utilizar colecciones de .NET, se implementaron las siguientes estructuras de datos desde cero en el proyecto **CrazyRisk.Core/DataStructures/**:

---

## 1. Lista<T> (Lista.cs)
**Propósito**: Lista dinámica genérica con redimensionamiento automático.

**Características**:
- Capacidad inicial: 4 elementos
- Redimensionamiento: Duplica capacidad cuando se llena
- Complejidad: O(1) amortizado para `Agregar()`, O(1) para acceso por índice

**Uso en el proyecto**:
- Almacenar jugadores (PlayerInfo)
- Almacenar cartas por jugador
- Almacenar IDs de territorios temporalmente

**Métodos principales**:
```csharp
void Agregar(T elemento)
void Remover(T elemento)
void RemoverEn(int indice)
void Limpiar()
bool Contiene(T elemento)
T[] ToArray()
```

---

## 2. Diccionario<TKey, TValue> (Diccionario.cs)
**Propósito**: Hash table con sondeo lineal para resolución de colisiones.

**Características**:
- Capacidad inicial: 16 buckets
- Redimensionamiento al 75% de carga
- Sondeo lineal para colisiones
- Complejidad: O(1) promedio para búsqueda/inserción

**Uso en el proyecto**:
- Mapear IDs de territorios a TerritoryState
- Mapear jugadores (int) a sus cartas (Lista<Card>)
- Almacenar máscaras de píxeles por territorio

**Métodos principales**:
```csharp
void Agregar(TKey key, TValue value)
bool TryGetValue(TKey key, out TValue value)
bool ContieneKey(TKey key)
void Remover(TKey key)
void ParaCada(Action<TKey, TValue> accion)
```

---

## 3. Cola<T> (Cola.cs)
**Propósito**: Cola circular (FIFO) para algoritmos de grafos.

**Características**:
- Implementación circular para eficiencia
- Capacidad inicial: 8 elementos
- No desperdicia espacio al desencolar
- Complejidad: O(1) para Encolar/Desencolar

**Uso en el proyecto**:
- Algoritmo BFS (Breadth-First Search) en `AreConnectedByOwnerPath()`
- Validar caminos de territorios propios para fortificación

**Métodos principales**:
```csharp
void Encolar(T elemento)
T Desencolar()
T Peek()
void Limpiar()
```

**Ejemplo de uso (BFS)**:
```csharp
var visited = new Conjunto<string>();
var q = new Cola<string>();
q.Encolar(fromId);
visited.Agregar(fromId);

while (q.Count > 0)
{
    var cur = q.Desencolar();
    // procesar vecinos...
}
```

---

## 4. Conjunto<T> (Conjunto.cs)
**Propósito**: HashSet para marcar elementos únicos sin duplicados.

**Características**:
- Similar a Diccionario pero solo almacena claves
- Sondeo lineal para colisiones
- Complejidad: O(1) promedio para Agregar/Contiene

**Uso en el proyecto**:
- Marcar territorios visitados en BFS
- Contar tipos distintos de cartas (Infantry, Cavalry, Artillery)

**Métodos principales**:
```csharp
bool Agregar(T elemento)  // retorna false si ya existe
bool Contiene(T elemento)
void Remover(T elemento)
void Limpiar()
```

---

## 5. Pila<T> (Pila.cs)
**Propósito**: Estructura LIFO para deshacer acciones.

**Características**:
- Capacidad inicial: 8 elementos
- Crece dinámicamente al duplicar
- Complejidad: O(1) para Apilar/Desapilar

**Uso en el proyecto**:
- Sistema de "Undo" en `ReinforcementService`
- Revertir colocaciones de refuerzos

**Métodos principales**:
```csharp
void Apilar(T elemento)
T Desapilar()
T Peek()
void Limpiar()
```

**Ejemplo de uso (Undo)**:
```csharp
private readonly Pila<(string territoryId, int amount)> _history = new Pila<(string, int)>();

public bool TryPlace(string territoryId, int amount)
{
    if (engine.PlaceReinforcements(territoryId, amount, out error))
    {
        _history.Apilar((territoryId, amount));
        return true;
    }
    return false;
}

public bool UndoLast()
{
    if (_history.Count == 0) return false;
    
    var (territoryId, amount) = _history.Desapilar();
    // revertir la acción...
    return true;
}
```

---

## Justificación del Diseño

### Redimensionamiento Dinámico
Todas las estructuras crecen dinámicamente para evitar límites artificiales:
- **Lista, Pila**: Duplican capacidad (amortizado O(1))
- **Diccionario, Conjunto**: Redimensionan al 75% de carga (mantiene O(1))
- **Cola**: Redimensiona copiando en orden lineal (mantiene eficiencia circular)

### Sondeo Lineal (Hash Tables)
Se eligió sondeo lineal sobre otras técnicas por:
- **Simplicidad**: Fácil de implementar y depurar
- **Caché-friendly**: Accesos secuenciales en memoria
- **Suficiente para el tamaño del proyecto**: <100 territorios, <10 jugadores

### Complejidad Temporal
Todas las operaciones críticas mantienen O(1) o O(n) según el caso de uso:
- Acceso a territorios por ID: O(1)
- BFS en fortificación: O(V + E) donde V=territorios, E=adyacencias
- Conteo de territorios propios: O(n) donde n=42

---

## Comparación con Colecciones .NET

| Estructura Custom | Equivalente .NET | Diferencias |
|------------------|------------------|-------------|
| Lista<T> | List<T> | Sin métodos LINQ, sin IEnumerable |
| Diccionario<K,V> | Dictionary<K,V> | Sondeo lineal vs. buckets separados |
| Cola<T> | Queue<T> | Circular manual vs. implementación interna |
| Conjunto<T> | HashSet<T> | Sin operaciones de conjuntos (Union, Intersect) |
| Pila<T> | Stack<T> | Idéntica funcionalidad básica |

---

## Archivos Modificados

Se reemplazaron colecciones de .NET en:
1. **GameEngine.cs**: 
   - `List<PlayerInfo>` → `Lista<PlayerInfo>`
   - `Dictionary<string, TerritoryState>` → `Diccionario<string, TerritoryState>`
   - `HashSet<string>` → `Conjunto<string>`
   - `Queue<string>` → `Cola<string>`

2. **CardsService.cs**:
   - `Dictionary<int, List<Card>>` → `Diccionario<int, Lista<Card>>`
   - `List<Card>` → `Lista<Card>`
   - `HashSet<CardKind>` → `Conjunto<CardKind>`

3. **ReinforcementService.cs** (si se implementa Undo):
   - `Stack<(string, int)>` → `Pila<(string, int)>`

---

## Conclusión

Las estructuras implementadas satisfacen todos los requerimientos del proyecto manteniendo eficiencia comparable a las colecciones estándar de .NET. El código es claro, documentado y fácil de mantener.

**Total de líneas de código**: ~400 líneas (5 archivos)
**Complejidad ciclomática**: Baja (métodos simples y focalizados)
**Cobertura funcional**: 100% de las necesidades del proyecto
