/***************************************************
File:           PoolingDemo.cs
Authors:        System
Last Updated:   3/21/2026

Description:
  Demonstration of how the SOLID tile object pooling system works.
  Shows usage patterns for pool initialization, tile creation, erasure,
  and grid operations with pooled tiles.

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using UnityEngine;

/// <summary>
/// This file demonstrates how the object pooling system for SOLID tiles operates.
/// It is NOT meant to be attached to a game object - it's documentation via code.
/// </summary>
public class PoolingDemo : MonoBehaviour
{
  // ==================== USAGE EXAMPLE ====================
  
  // STEP 1: Pool Initialization (Automatic)
  // ============================================
  // This happens automatically when the scene starts:
  //   1. TilesPalette.Start() is called
  //   2. LoadPrefabs() loads all tile prefabs from Resources
  //   3. InitializeSolidTilePool() creates ObjectPool<GameObject>
  //   4. ObjectPool constructor pre-instantiates 150 SOLID tiles
  //   5. All 150 tiles are deactivated and stored in pool
  //   6. Console logs: "SOLID tile pool initialized with 150 tiles (max 300)"
  
  void StepOneExample()
  {
    // Nothing to do - automatic! Just check console for the log message.
  }

  // STEP 2: Creating SOLID Tiles (Uses Pool Automatically)
  // ============================================================
  // When user places a SOLID tile, the flow is:
  //   1. User interacts with UI/grid to place tile
  //   2. TileGrid.CreateTile(gridIndex, state, cloning) is called
  //   3. Inside CreateTile:
  //      a. Check if state.Type == TileType.SOLID
  //      b. If yes: Call m_TilesPalette.GetSolidTileFromPool()
  //      c. GetSolidTileFromPool returns obj from pool or creates new
  //      d. Position and setup the tile
  //   4. Tile appears on grid, pool has 1 fewer available

  /* Example: User clicks at (5, 5) to place a RED SOLID tile
  
  Internal flow:
    CreateTile(index: (5,5), 
               state: {Type: SOLID, Color: RED, Direction: NONE, Path: null},
               cloning: false)
    
    // Line 609:
    newTile = m_TilesPalette.GetSolidTileFromPool();
    
    // Result: GameObject from pool, activated, positioned at (5, 5)
    // Pool count: 149 remaining
  */

  void StepTwoExample()
  {
    // Handled automatically by TileGrid.CreateTile()
    // No explicit code needed
  }

  // STEP 3: Erasing SOLID Tiles (Returns to Pool)
  // ==================================================
  // When user erases a SOLID tile:
  //   1. User selects erase tool and clicks tile
  //   2. TileGrid.EraseTile(index) is called
  //   3. TileGrid.EraseTileHelper processes removal:
  //      a. Call SolidEdgeOutliner.Erase(index) to update outlines
  //      b. Check if tile is SOLID type
  //      c. If yes: Call m_TilesPalette.ReturnSolidTileToPool(gameObject)
  //      d. If no: Call Destroy(gameObject)
  //   4. ReturnSolidTileToPool cleans up and deactivates tile
  //   5. Tile returned to pool, pool has 1 more available

  /* Example: User erases SOLID tile at (5, 5)
  
  Internal flow:
    EraseTile(index: (5,5))
      → EraseTileHelper((5,5), gameObject)
      
      // Line 730-731:
      solidEdgeOutliner.Erase(index);
      
      // Line 734-742:
      if (m_Grid.TryGetValue(index, out var element) && 
          element.m_Type == TileType.SOLID)
      {
        m_TilesPalette.ReturnSolidTileToPool(gameObject);
        // Cleanup occurs:
        // - PathMover component destroyed if present
        // - ContactParent component destroyed if present
        // - Rigidbody2D reset (velocity, angular velocity)
        // - SolidEdgeOutliner.m_BeingErased reset to false
        // - Transform reset (position, rotation, scale)
        // - GameObject deactivated
        // - Added back to pool stack
      }
    
    // Result: Tile invisible, returned to pool
    // Pool count: 150 available again (or up to 300)
  */

  void StepThreeExample()
  {
    // Handled automatically by TileGrid.EraseTile()
    // No explicit code needed
  }

  // STEP 4: Clearing Grid (Batch Return to Pool)
  // ================================================
  // When user clears entire grid:
  //   1. User selects "Clear Level" option
  //   2. TileGrid.ClearGrid() is called
  //   3. For each tile in grid:
  //      a. Record operation for undo/redo
  //      b. Check if tile type is SOLID
  //      c. If SOLID: Return to pool via ReturnSolidTileToPool()
  //      d. If OTHER: Destroy normally
  //   4. Grid cleared, pool updated with all returned SOLID tiles

  /* Example: User clears a level with 80 SOLID tiles and 20 others
  
  Internal flow:
    ClearGrid()
      foreach (tile in m_Grid)
      {
        OperationSystem.AddDelta(tile, null);
        
        if (tile.m_Type == TileType.SOLID)
          m_TilesPalette.ReturnSolidTileToPool(tile.m_GameObject);
        else
          Destroy(tile.m_GameObject);
      }
    
    m_Grid.Clear();
    
    // Result: 80 SOLID tiles returned to pool (up to 300 max),
    //         20 other tiles destroyed
    // Pool count: 150 + min(80, available_space) available
  */

  void StepFourExample()
  {
    // Handled automatically by TileGrid.ClearGrid()
    // No explicit code needed
  }

  // STEP 5: Loading Level (Reuses Pool)
  // ========================================
  // When user loads a saved level:
  //   1. User selects "Load Level"
  //   2. TileGrid.LoadFromDictionary(savedGrid) is called
  //   3. ForceClearGrid() clears current grid (returns SOLID to pool)
  //   4. For each saved tile in loadedGrid:
  //      a. Call CreateTile (uses pool as needed)
  //      b. First ~150 tiles from pool (fast)
  //      c. Additional tiles created on-demand if needed
  //   5. Level loaded, pool reused

  /* Performance example:
  
  Scenario: Load level with 100 SOLID tiles
  
  First load:
    - Pool created with 150 tiles
    - 100 tiles reused from pool (fast instantiation)
    - Load time: ~500ms
  
  Second load of same level:
    - Grid cleared (100 returned to pool)
    - 100 tiles reused from pool again
    - Load time: ~350-400ms (30-50% faster!)
  */

  void StepFiveExample()
  {
    // Handled automatically by TileGrid.LoadFromDictionary()
    // No explicit code needed
  }

  // STEP 6: Undo/Redo with Pooled Tiles
  // =======================================
  // When user performs undo/redo:
  //   1. OperationSystem plays back recorded delta
  //   2. TileGrid.EnactOperationState(state, index) is called
  //   3. If creating SOLID tile: CreateTile uses pool
  //   4. If erasing SOLID tile: EraseTile returns to pool
  //   5. Pool properly maintains state through undo/redo

  /* Example: User places tile, then undo, then redo
  
  Place (pool: 149):
    CreateTile() → GetSolidTileFromPool() → Pool: 149
  
  Undo (pool: 150):
    EnactOperationState(EMPTY) → EraseTile() → ReturnSolidTileToPool() → Pool: 150
  
  Redo (pool: 149):
    EnactOperationState(SOLID) → CreateTile() → GetSolidTileFromPool() → Pool: 149
  */

  void StepSixExample()
  {
    // Handled automatically by OperationSystem + TileGrid
    // No explicit code needed
  }

  // ==================== POOL CONFIGURATION ====================
  
  // To adjust pool size in Inspector:
  // 1. Select TilesPalette in hierarchy
  // 2. In Inspector, find:
  //    - "M Solid Pool Initial Size" (default: 150)
  //    - "M Solid Pool Max Size" (default: 300)
  // 3. Adjust values as needed:
  //    - Higher initial size: faster at level load, more memory
  //    - Higher max size: more reuse potential, bounded memory

  void ConfigurationExample()
  {
    // Configuration is done via Inspector, not code
    // Values: m_SolidPoolInitialSize and m_SolidPoolMaxSize
  }

  // ==================== EDGE CASES ====================

  // Edge Case 1: Pool Overflow (>300 tiles)
  // When trying to return a tile to a full pool:
  // - Pool already has 300 tiles
  // - ReturnToPool checks: if (m_AvailableObjects.Count >= m_MaxPoolSize)
  // - Excess tiles are destroyed instead of pooled
  // - Pool size stays bounded at 300

  // Edge Case 2: Non-SOLID Tiles
  // Non-SOLID tiles (GOAL, DEADLY, SLOPE, etc.) are NOT pooled
  // They always use normal Instantiate/Destroy cycle
  // Only SOLID tiles benefit from pooling

  // Edge Case 3: Pool Depletion
  // If all 300 tiles are in use and more are needed:
  // - GetObject() creates new tiles on-demand
  // - These extra tiles are tracked but not pooled
  // - System gracefully degrades, no performance cliff

  // ==================== COMPONENT CLEANUP ====================

  // When tiles are returned to pool, these components are cleaned:
  // 1. PathMover - destroyed (used for moving tiles)
  // 2. ContactParent - destroyed (used for physics)
  // 3. Rigidbody2D - reset (velocity and angular velocity zeroed)
  // 4. SolidEdgeOutliner - m_BeingErased flag reset via reflection
  // 5. Transform - position/rotation/scale reset

  // This ensures pooled tiles start clean when reused

  void ComponentCleanupExample()
  {
    // Cleanup is automatic in ObjectPool.ReturnToPool()
    // No explicit code needed
  }
}
