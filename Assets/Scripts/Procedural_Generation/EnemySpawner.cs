using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using cowsins;

// Fills the dungeon's rooms with enemies after the floor is built and the NavMesh is
// baked. For each room it picks a random number of enemies, finds valid spots on the
// walkable surface, and drops the enemy prefabs there.
public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefabs")]
    [SerializeField] private GameObject[] enemyPrefabs;   // enemy types to choose from at random

    [Header("Spawn Settings")]
    [SerializeField] private int   maxEnemiesPerRoom = 2; // upper bound of the random count per room
    [SerializeField] private int   minEnemiesPerRoom = 0; // lower bound (0 means a room can be empty)
    [SerializeField] private float spawnHeight       = 1f;// how high above the tile to start, before snapping down to the NavMesh
    [SerializeField] private float sampleRadius      = 3f;// how far to search for the nearest NavMesh point when snapping
    [SerializeField] private bool  skipStartRoom     = true; // keep the player's start room enemy-free
    [SerializeField] private bool  skipExitRoom      = true; // keep the exit "safe room" enemy-free

    [Header("References")]
    [SerializeField] private Transform enemyParent;       // spawned enemies are parented here so they're easy to clear

    // Multiplies each enemy's starting health on tougher floors. 1 = unchanged.
    private float enemyHealthMultiplier = 1f;

    // EnemyHealth's "maxHealth" value is marked protected by Cowsins (not directly
    // accessible from outside). "Reflection" is a C# feature that reaches into a class and
    // grabs a field by its name at runtime — a workaround that avoids editing the vendored
    // Cowsins script. The field is looked up once and cached (static + readonly) for reuse.
    private static readonly FieldInfo MaxHealthField =
        typeof(EnemyHealth).GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Copies this floor's enemy settings in. When no config is applied the
    /// inspector defaults remain in effect.</summary>
    public void ApplyConfig(FloorConfig config)
    {
        if (config == null) return;

        if (config.enemyPrefabs != null && config.enemyPrefabs.Length > 0)
            enemyPrefabs = config.enemyPrefabs;

        // The config only has one "enemies per room" number. It maps to the MAX of the
        // random range; the MIN keeps whatever was set in the inspector.
        maxEnemiesPerRoom = config.enemiesPerRoom;

        enemyHealthMultiplier = config.enemyHealthMultiplier;
    }

    public void SpawnEnemies(DungeonGenerator generator, float tileSize)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogWarning("[EnemySpawner] No enemy prefabs assigned — skipping spawn.");
            return;
        }

        int totalSpawned = 0;
        int roomsUsed    = 0;

        // A HashSet records which grid cells are already used, so two enemies in the same
        // room never spawn on top of each other. Cleared and reused per room.
        var usedCells = new HashSet<Vector2Int>();

        foreach (DungeonGenerator.Room room in generator.Rooms)
        {
            if (skipStartRoom && room == generator.StartRoom) continue;
            if (skipExitRoom  && room == generator.ExitRoom)  continue;

            // Roll a random enemy count for this room. (+1 because Random.Range's upper
            // bound is exclusive for ints, so this includes maxEnemiesPerRoom itself.)
            int count = Random.Range(minEnemiesPerRoom, maxEnemiesPerRoom + 1);
            if (count == 0) continue;

            // Enemies only spawn on a room's INNER cells (not against the walls), and
            // each needs its own cell. A WxH room has (W-2)x(H-2) inner cells, so cap
            // the count to that — better to spawn fewer than to stack two on one tile.
            int interiorCells = Mathf.Max(0, (room.Width - 2) * (room.Height - 2));
            count = Mathf.Min(count, interiorCells);
            if (count == 0) continue;

            usedCells.Clear();
            roomsUsed++;

            for (int i = 0; i < count; i++)
            {
                if (!TryReserveInteriorCell(room, usedCells, out Vector2Int gridPos))
                    break; // couldn't find an unused cell (shouldn't happen given the cap above)

                // Turn the grid cell into a world position and lift it up a bit.
                Vector3 worldPos = DungeonGenerator.GridToWorld(gridPos.x, gridPos.y, tileSize)
                                   + Vector3.up * spawnHeight;

                // "Snap" that position onto the nearest point of the baked NavMesh, so the
                // enemy starts on walkable ground. If there's no NavMesh nearby, skip it.
                if (!NavMesh.SamplePosition(worldPos, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[EnemySpawner] No NavMesh within {sampleRadius} of {worldPos} " +
                                     $"(room cell {gridPos}) — skipping enemy.");
                    continue;
                }

                Vector3 spawnPosition = hit.position; // the snapped-onto-NavMesh point

                GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
                if (prefab == null) continue;

                // Instantiate = create a copy of the prefab in the scene.
                GameObject enemy = Instantiate(prefab, spawnPosition, Quaternion.identity, enemyParent);

                // Bump up health for harder floors. This runs NOW, before the enemy's own
                // Start() runs, because Start() copies maxHealth into current health — so
                // changing maxHealth first lets the new value carry through automatically.
                ApplyHealthMultiplier(enemy);

                // Warp() officially places a NavMeshAgent onto the mesh so it's properly
                // registered, rather than just sitting at the assigned position.
                if (enemy.TryGetComponent(out NavMeshAgent agent))
                    agent.Warp(spawnPosition);

                totalSpawned++;
            }
        }

        Debug.Log($"[EnemySpawner] Spawned {totalSpawned} enemies across {roomsUsed} rooms.");
    }

    // Multiplies a freshly spawned enemy's max health by enemyHealthMultiplier (using
    // the reflection field grabbed up top). Does nothing if the multiplier is 1 or the
    // enemy has no EnemyHealth. Must run before the enemy's Start() copies max → current.
    private void ApplyHealthMultiplier(GameObject enemy)
    {
        if (Mathf.Approximately(enemyHealthMultiplier, 1f)) return; // 1 = no change, skip the work
        if (MaxHealthField == null) return;                         // reflection lookup failed earlier

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health == null) return;

        // Read the current max via reflection, multiply it, and write it back.
        float baseMax = (float)MaxHealthField.GetValue(health);
        MaxHealthField.SetValue(health, baseMax * enemyHealthMultiplier);
    }

    // Picks a random inner cell of the room not yet used this room. Returns false if no free
    // one is found. usedCells.Add returns false when the cell is already in the set, which is
    // how a repeat is detected (and rejected).
    private bool TryReserveInteriorCell(DungeonGenerator.Room room,
                                        HashSet<Vector2Int> usedCells,
                                        out Vector2Int cell)
    {
        // The count is already capped to the number of free cells, so a few tries per enemy
        // (4x the cell count) is plenty to land on an unused one — and the limit guarantees
        // the loop always ends instead of spinning forever.
        int maxAttempts = (room.Width - 2) * (room.Height - 2) * 4;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            cell = room.RandomInteriorPoint();
            if (usedCells.Add(cell)) return true; // Add succeeds only if it wasn't already taken
        }

        cell = default;
        return false;
    }

    // Removes every enemy from the previous floor. Called before a new floor is built so old
    // enemies don't pile up. DestroyImmediate deletes right away (rather than at end of
    // frame); the loop runs backwards because the child list shrinks as items are deleted.
    public void ClearEnemies()
    {
        if (enemyParent == null) return;
        for (int i = enemyParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(enemyParent.GetChild(i).gameObject);
    }
}
