/*
 * ============================================================================
 * GUÍA DE IMPLEMENTACIÓN DE RETOS - PROYECTO CRAZYRISK
 * ============================================================================
 * 
 * Este archivo contiene las implementaciones comentadas de los 4 retos posibles
 * que el profesor puede solicitar durante la revisión del proyecto.
 * 
 * INSTRUCCIONES:
 * - Lee cada reto y entiende la lógica
 * - Cuando el profesor lo solicite, copia el código correspondiente
 * - Pégalo en el archivo indicado
 * - Descomenta y adapta según las especificaciones del profesor
 * 
 * ============================================================================
 */

using System;
using CrazyRisk.Core;
using CrazyRiskGame.Game.Services;

namespace CrazyRisk.Retos
{
    // ========================================================================
    // RETO 1: PERMITIR JUGADOR ADICIONAL CON CANTIDAD DE TROPAS VARIABLE
    // ========================================================================
    // 
    // ARCHIVOS A MODIFICAR: 
    // - Source\CrazyRisk.Core\GameEngine.cs
    //
    // EXPLICACIÓN:
    // El constructor de GameEngine tiene un array que define las tropas iniciales
    // según la cantidad de jugadores. Para permitir más jugadores o cambiar las
    // tropas, hay que modificar el constructor y el array startPools.
    // 
    // UBICACIÓN: GameEngine.cs línea ~214

    /*
    // CÓDIGO ORIGINAL:
    public GameEngine(Map map, Lista<PlayerInfo> players, int? seed = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        rng = new Random(seed ?? Environment.TickCount);

        if (players == null || players.Count < 2)
            throw new ArgumentException("Se requieren al menos 2 jugadores.", nameof(players));
        
        // ... más código ...
        
        int[] startPools = { 0, 0, 40, 35, 30, 25, 20 };
        int pool = players.Count < startPools.Length ? startPools[players.Count] : 20;
        
        // ... resto del constructor ...
    }
    */

    /*
    // CÓDIGO MODIFICADO PARA RETO 1:
    // Reemplazar el constructor completo con esta versión:

    public GameEngine(Map map, Lista<PlayerInfo> players, int? seed = null, int? tropasPersonalizadas = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        rng = new Random(seed ?? Environment.TickCount);

        // CAMBIO 1: Permitir hasta 6 jugadores (o el número que el profesor indique)
        if (players == null || players.Count < 2 || players.Count > 6)
            throw new ArgumentException("Se requieren entre 2 y 6 jugadores.", nameof(players));

        State.Players = new Lista<PlayerInfo>();
        for (int i = 0; i < players.Count; i++)
            State.Players.Agregar(players[i]);

        // Recolecta ids de territorios desde Map
        var terrIds = new Lista<string>();
        foreach (var id in Map.GetTerritoryIds())
            terrIds.Agregar(id);
        
        if (terrIds.Count == 0)
            throw new InvalidOperationException("El mapa no contiene territorios legibles (Id/id).");

        Shuffle(terrIds);

        State.Territories = new Diccionario<string, TerritoryState>();
        int pIndex = 0;
        for (int i = 0; i < terrIds.Count; i++)
        {
            var tid = terrIds[i];
            var owner = players[pIndex].Id;
            State.Territories[tid] = new TerritoryState(tid, owner, troops: 1);
            pIndex = (pIndex + 1) % players.Count;
        }

        // CAMBIO 2: Sistema flexible de tropas iniciales
        int[] startPools = { 0, 0, 40, 35, 30, 25, 20, 18 }; // Agregado índice 7 para 7 jugadores
        int pool;
        
        if (tropasPersonalizadas.HasValue)
        {
            // Si el profesor especifica una cantidad, usar esa
            pool = tropasPersonalizadas.Value;
        }
        else
        {
            // Si no, usar el array por defecto
            pool = players.Count < startPools.Length ? startPools[players.Count] : 15;
        }

        var current = players[0].Id;
        State.CurrentPlayerId = current;
        State.Phase = Phase.Reinforcement;
        State.ReinforcementsRemaining = pool;
    }
    
    // CÓMO USAR:
    // Sin cambios: new GameEngine(map, players) -> Usa tropas por defecto
    // Con tropas custom: new GameEngine(map, players, null, 50) -> Todos los jugadores empiezan con 50 tropas
    */


    // ========================================================================
    // RETO 2: NUEVA CONDICIÓN DE VICTORIA - PRIMER JUGADOR EN CONQUISTAR UN CONTINENTE
    // ========================================================================
    //
    // ARCHIVOS A MODIFICAR:
    // - Source\CrazyRisk.Core\GameEngine.cs (agregar métodos de victoria)
    // - Source\CrazyRisk.Game\GamePlayController.cs (llamar verificación)
    //
    // EXPLICACIÓN:
    // Actualmente el juego no verifica condiciones de victoria. Hay que agregar
    // métodos para verificar si un jugador conquistó un continente completo.

    /*
    // CÓDIGO PARA AGREGAR EN GameEngine.cs (después de los métodos existentes, línea ~500)

    // ========================= CONDICIONES DE VICTORIA =========================

    /// <summary>
    /// Verifica si algún jugador ha ganado conquistando un continente completo.
    /// Requiere ContinentBonusService para conocer la definición de continentes.
    /// </summary>
    /// <returns>ID del jugador ganador, o null si nadie ha ganado aún</returns>
    public int? CheckVictoryByContinent(ContinentBonusService continentService)
    {
        if (continentService == null) return null;
        
        foreach (var player in State.Players)
        {
            var bonus = continentService.GetBonusForPlayer(this, player.Id, out var owned);
            if (owned.Count > 0)
            {
                // Este jugador controla al menos un continente completo
                return player.Id;
            }
        }
        return null; // Nadie ha ganado aún
    }

    /// <summary>
    /// Verifica victoria por eliminación (solo queda un jugador con territorios).
    /// </summary>
    /// <returns>True si solo queda un jugador vivo</returns>
    public bool CheckVictoryByElimination(out int winnerId)
    {
        int playersAlive = 0;
        int lastPlayerId = -1;
        
        for (int i = 0; i < State.Players.Count; i++)
        {
            var player = State.Players[i];
            int territoriosControlados = CountOwned(player.Id);
            if (territoriosControlados > 0)
            {
                playersAlive++;
                lastPlayerId = player.Id;
            }
        }
        
        winnerId = lastPlayerId;
        return playersAlive == 1;
    }

    /// <summary>
    /// Verifica victoria por dominación total (controla todos los territorios).
    /// </summary>
    public bool CheckVictoryByDomination(out int winnerId)
    {
        winnerId = -1;
        int totalTerritories = 0;
        State.Territories.ParaCada((id, state) => { totalTerritories++; });
        
        for (int i = 0; i < State.Players.Count; i++)
        {
            var player = State.Players[i];
            int owned = CountOwned(player.Id);
            if (owned == totalTerritories)
            {
                winnerId = player.Id;
                return true;
            }
        }
        return false;
    }
    */

    /*
    // CÓDIGO PARA AGREGAR EN GamePlayController.cs (después de cada acción importante)
    // Ubicación: Después de EndTurn(), Attack(), etc.

    // En el método Update() o después de acciones importantes:
    
    private void CheckWinConditions()
    {
        // OPCIÓN 1: Victoria por continente (si el profesor pide este reto)
        var continentWinner = _engine.CheckVictoryByContinent(_continentBonusService);
        if (continentWinner.HasValue)
        {
            var winner = GetPlayerName(continentWinner.Value);
            ShowVictoryScreen($"¡{winner} ha ganado conquistando un continente completo!");
            return;
        }

        // OPCIÓN 2: Victoria por eliminación
        if (_engine.CheckVictoryByElimination(out int eliminationWinner))
        {
            var winner = GetPlayerName(eliminationWinner);
            ShowVictoryScreen($"¡{winner} ha ganado eliminando a todos los oponentes!");
            return;
        }

        // OPCIÓN 3: Victoria por dominación total
        if (_engine.CheckVictoryByDomination(out int dominationWinner))
        {
            var winner = GetPlayerName(dominationWinner);
            ShowVictoryScreen($"¡{winner} ha ganado conquistando todos los territorios!");
            return;
        }
    }

    private string GetPlayerName(int playerId)
    {
        for (int i = 0; i < _engine.State.Players.Count; i++)
        {
            if (_engine.State.Players[i].Id == playerId)
                return _engine.State.Players[i].Name;
        }
        return "Jugador desconocido";
    }

    private void ShowVictoryScreen(string message)
    {
        // Implementar UI de victoria aquí
        // Detener el juego, mostrar mensaje, botón de salir, etc.
    }
    */


    // ========================================================================
    // RETO 3: CAMBIAR FÓRMULA DE CÁLCULO DE TROPAS
    // ========================================================================
    //
    // ARCHIVOS A MODIFICAR:
    // - Source\CrazyRisk.Core\GameEngine.cs (método CalculateReinforcements)
    //
    // EXPLICACIÓN:
    // La fórmula actual es: max(3, territorios/3) + bonus_continentes
    // Se puede modificar para incluir:
    // - Territorios fronterizos (que tienen vecinos enemigos)
    // - Cantidad de tríos de cartas canjeados
    // - Cualquier otro factor dinámico

    /*
    // CÓDIGO ORIGINAL EN GameEngine.cs (línea ~285):
    
    public int CalculateReinforcements(int playerId)
    {
        int owned = CountOwned(playerId);
        int baseReinf = Math.Max(3, owned / 3);
        int continentBonus = 0; // se puede ampliar con bonus por continentes
        return baseReinf + continentBonus;
    }
    */

    /*
    // CÓDIGO MODIFICADO PARA RETO 3:
    // Reemplazar el método CalculateReinforcements con esta versión extendida:

    /// <summary>
    /// Calcula refuerzos considerando múltiples factores:
    /// - Base: max(3, territorios/3)
    /// - Bonus por continentes (si se usa ContinentBonusService)
    /// - Bonus por territorios fronterizos (territorios con vecinos enemigos)
    /// - Bonus por tríos de cartas canjeados en el juego
    /// </summary>
    public int CalculateReinforcements(int playerId, int triosCanjeados = 0)
    {
        // FACTOR 1: Base de territorios (fórmula clásica de Risk)
        int owned = CountOwned(playerId);
        int baseReinf = Math.Max(3, owned / 3);
        
        // FACTOR 2: Bonus por territorios fronterizos
        // Un territorio fronterizo es aquel que tiene al menos un vecino enemigo
        int fronterizos = CountFrontierTerritories(playerId);
        int frontierBonus = fronterizos / 5; // 1 tropa extra por cada 5 territorios fronterizos
        
        // FACTOR 3: Bonus por tríos canjeados (histórico del juego)
        // Esto recompensa a jugadores que han canjeado muchas cartas
        int trioBonus = triosCanjeados * 2; // 2 tropas extra por cada trío canjeado
        
        // FACTOR 4: Bonus por continentes (usar ContinentBonusService si está disponible)
        int continentBonus = 0; 
        
        // Total de refuerzos
        int total = baseReinf + continentBonus + frontierBonus + trioBonus;
        
        return total;
    }

    /// <summary>
    /// Cuenta cuántos territorios del jugador tienen al menos un vecino enemigo.
    /// Un territorio fronterizo es más vulnerable pero estratégicamente importante.
    /// </summary>
    private int CountFrontierTerritories(int playerId)
    {
        int count = 0;
        
        State.Territories.ParaCada((tid, tstate) =>
        {
            if (tstate.OwnerId == playerId)
            {
                // Verificar si tiene vecinos enemigos
                var neighbors = Map.GetNeighbors(tid);
                bool hasFrontier = false;
                
                foreach (var nid in neighbors)
                {
                    if (State.Territories.TryGetValue(nid, out var nstate) && nstate.OwnerId != playerId)
                    {
                        hasFrontier = true;
                        break; // Ya contamos este territorio como fronterizo
                    }
                }
                
                if (hasFrontier)
                    count++;
            }
        });
        
        return count;
    }

    // NOTA: Para usar el parámetro triosCanjeados, hay que llevar contador en GameState
    // Agregar en GameState.cs:
    // public int TotalTriosCanjeados { get; set; } = 0;
    // 
    // Y en CardsService.TradeTriplet(), incrementarlo:
    // engine.State.TotalTriosCanjeados++;
    */

    /*
    // VARIANTE ALTERNATIVA - Fórmula completamente diferente:
    // Si el profesor pide una fórmula más radical:

    public int CalculateReinforcementsAlternative(int playerId)
    {
        int owned = CountOwned(playerId);
        int fronterizos = CountFrontierTerritories(playerId);
        
        // Fórmula alternativa: 
        // 2 tropas por cada 3 territorios + 1 tropa por cada 2 territorios fronterizos
        int baseReinf = (owned * 2) / 3;
        int frontierBonus = fronterizos / 2;
        
        // Mínimo 2 tropas siempre
        return Math.Max(2, baseReinf + frontierBonus);
    }
    */


    // ========================================================================
    // RETO 4: JUGADOR ESPECTADOR (OBSERVADOR SIN PARTICIPAR)
    // ========================================================================
    //
    // ARCHIVOS A MODIFICAR:
    // - Source\CrazyRisk.Core\GameEngine.cs (clase PlayerInfo)
    // - Source\CrazyRisk.Game\Play\Net\LAN\LanLobbyModels.cs
    // - Source\CrazyRisk.Game\Play\Net\LAN\LanLobbyService.cs
    // - Archivos de UI para mostrar espectadores
    //
    // EXPLICACIÓN:
    // Un espectador es un usuario conectado que ve el juego en tiempo real
    // pero no puede realizar acciones. Recibe actualizaciones del estado
    // pero no participa en la distribución de territorios ni turnos.

    /*
    // PASO 1: MODIFICAR PlayerInfo EN GameEngine.cs (línea ~154)
    
    // CÓDIGO ORIGINAL:
    public sealed class PlayerInfo
    {
        public int Id { get; }
        public string Name { get; }

        public PlayerInfo(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
    */

    /*
    // CÓDIGO MODIFICADO:
    public sealed class PlayerInfo
    {
        public int Id { get; }
        public string Name { get; }
        public bool IsSpectator { get; } // NUEVO: Indica si es espectador

        public PlayerInfo(int id, string name, bool isSpectator = false)
        {
            Id = id;
            Name = name;
            IsSpectator = isSpectator;
        }
    }
    */

    /*
    // PASO 2: MODIFICAR CONSTRUCTOR DE GameEngine (línea ~214)
    
    // En el constructor, filtrar espectadores para distribución de territorios:

    public GameEngine(Map map, Lista<PlayerInfo> players, int? seed = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        rng = new Random(seed ?? Environment.TickCount);

        // Guardar TODOS los jugadores (incluyendo espectadores)
        State.Players = new Lista<PlayerInfo>();
        for (int i = 0; i < players.Count; i++)
            State.Players.Agregar(players[i]);

        // Filtrar solo jugadores ACTIVOS para distribución de territorios
        var activePlayers = new Lista<PlayerInfo>();
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].IsSpectator)
                activePlayers.Agregar(players[i]);
        }

        if (activePlayers.Count < 2)
            throw new ArgumentException("Se requieren al menos 2 jugadores activos (no espectadores).", nameof(players));

        // Recolecta ids de territorios desde Map
        var terrIds = new Lista<string>();
        foreach (var id in Map.GetTerritoryIds())
            terrIds.Agregar(id);
        
        if (terrIds.Count == 0)
            throw new InvalidOperationException("El mapa no contiene territorios legibles (Id/id).");

        Shuffle(terrIds);

        State.Territories = new Diccionario<string, TerritoryState>();
        int pIndex = 0;
        
        // USAR SOLO JUGADORES ACTIVOS para distribución
        for (int i = 0; i < terrIds.Count; i++)
        {
            var tid = terrIds[i];
            var owner = activePlayers[pIndex].Id;
            State.Territories[tid] = new TerritoryState(tid, owner, troops: 1);
            pIndex = (pIndex + 1) % activePlayers.Count;
        }

        int[] startPools = { 0, 0, 40, 35, 30, 25, 20 };
        int pool = activePlayers.Count < startPools.Length ? startPools[activePlayers.Count] : 20;

        // El primer jugador ACTIVO (no espectador) es el que empieza
        var current = activePlayers[0].Id;
        State.CurrentPlayerId = current;
        State.Phase = Phase.Reinforcement;
        State.ReinforcementsRemaining = pool;
    }
    */

    /*
    // PASO 3: AGREGAR MODELOS EN LanLobbyModels.cs
    
    // Agregar al final del archivo:

    /// <summary>
    /// Información de un jugador que se une al lobby.
    /// </summary>
    public sealed class PlayerJoinInfo
    {
        public string Name { get; init; } = "";
        public bool IsSpectator { get; init; } = false; // Indica si se une como espectador
        public DateTime JoinTime { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Lista de jugadores conectados (activos + espectadores).
    /// </summary>
    public sealed class LobbyPlayerList
    {
        public List<PlayerJoinInfo> ActivePlayers { get; init; } = new();
        public List<PlayerJoinInfo> Spectators { get; init; } = new();
    }
    */

    /*
    // PASO 4: MODIFICAR LanLobbyService.cs para manejar espectadores
    
    // Agregar lista de espectadores:
    private readonly List<IPEndPoint> _spectators = new();
    private readonly List<IPEndPoint> _activePlayers = new();

    // Método para unirse como espectador:
    public void JoinAsSpectator(string name)
    {
        // Enviar mensaje al host: "JOIN_SPECTATOR|nombre"
        var msg = $"JOIN_SPECTATOR|{name}";
        SendToHost(msg);
    }

    // En el host, al recibir mensajes:
    private void OnMessageReceived(IPEndPoint sender, string message)
    {
        if (message.StartsWith("JOIN_SPECTATOR|"))
        {
            var name = message.Substring("JOIN_SPECTATOR|".Length);
            _spectators.Add(sender);
            OnEvent?.Invoke(LobbyEvent.Info($"Espectador conectado: {name}"));
            
            // Enviar estado actual del juego al espectador
            BroadcastGameStateToSpectator(sender);
        }
        else if (message.StartsWith("JOIN_PLAYER|"))
        {
            var name = message.Substring("JOIN_PLAYER|".Length);
            _activePlayers.Add(sender);
            OnEvent?.Invoke(LobbyEvent.ClientConnected(sender));
        }
        // ... otros mensajes ...
    }

    // Método para enviar actualizaciones solo a espectadores:
    public void BroadcastGameStateToSpectators(GameState state)
    {
        string stateJson = state.ToJson();
        foreach (var spectator in _spectators)
        {
            SendToClient(spectator, $"STATE_UPDATE|{stateJson}");
        }
    }

    // Llamar este método después de cada acción del juego:
    // - Después de cada turno
    // - Después de cada ataque
    // - Después de cada refuerzo
    // Esto mantiene a los espectadores sincronizados en tiempo real
    */

    /*
    // PASO 5: UI PARA ESPECTADORES
    
    // En la pantalla de lobby, agregar opción:
    // [Unirse como Jugador] [Unirse como Espectador]
    
    // En la pantalla de juego, mostrar lista de espectadores:
    // Jugadores: Jugador1, Jugador2, Jugador3
    // Espectadores: Observador1, Observador2
    
    // Los espectadores ven todo el tablero sin restricciones:
    // - No pueden hacer clic en territorios
    // - No tienen interfaz de acciones
    // - Solo visualización del estado actual
    */


    // ========================================================================
    // RESUMEN DE ARCHIVOS A MODIFICAR POR RETO
    // ========================================================================
    /*
    
    RETO 1: Jugador adicional con tropas variables
    -----------------------------------------------
    ✓ GameEngine.cs (constructor) - Línea ~214
      - Agregar parámetro tropasPersonalizadas
      - Modificar validación de cantidad de jugadores
      - Ajustar array startPools
    
    
    RETO 2: Victoria por continente
    --------------------------------
    ✓ GameEngine.cs - Línea ~500 (al final de la clase)
      - Agregar CheckVictoryByContinent()
      - Agregar CheckVictoryByElimination()
      - Agregar CheckVictoryByDomination()
    
    ✓ GamePlayController.cs - En método Update() o después de acciones
      - Agregar CheckWinConditions()
      - Agregar ShowVictoryScreen()
    
    
    RETO 3: Cambiar fórmula de tropas
    ----------------------------------
    ✓ GameEngine.cs - Línea ~285
      - Modificar CalculateReinforcements()
      - Agregar CountFrontierTerritories()
      - Agregar parámetro triosCanjeados
    
    ✓ GameState.cs (opcional)
      - Agregar propiedad TotalTriosCanjeados
    
    
    RETO 4: Jugador espectador
    ---------------------------
    ✓ GameEngine.cs - Línea ~154
      - Modificar clase PlayerInfo (agregar IsSpectator)
      - Modificar constructor para filtrar espectadores
    
    ✓ LanLobbyModels.cs
      - Agregar PlayerJoinInfo
      - Agregar LobbyPlayerList
    
    ✓ LanLobbyService.cs
      - Agregar lista _spectators
      - Agregar método JoinAsSpectator()
      - Agregar método BroadcastGameStateToSpectators()
      - Modificar OnMessageReceived()
    
    ✓ UI (varios archivos)
      - Agregar botón "Unirse como Espectador"
      - Mostrar lista de espectadores en pantalla de juego
      - Deshabilitar controles para espectadores
    
    */
}
