using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefabs")]
    [SerializeField] private GameObject[] enemyPrefabs;

    [Header("Spawn Settings")]
    [SerializeField] private int   maxEnemiesPerRoom = 2;
    [SerializeField] private int   minEnemiesPerRoom = 0;
    [SerializeField] private float spawnHeight       = 1f;
    [SerializeField] private bool  skipStartRoom     = true;
    [SerializeField] private bool  skipExitRoom      = true;

    [Header("References")]
    [SerializeField] private Transform enemyParent;

    public void SpawnEnemies(DungeonGenerator generator, float tileSize)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogWarning("[EnemySpawner] No enemy prefabs assigned — skipping spawn.");
            return;
        }

        int totalSpawned = 0;
        int roomsUsed    = 0;

        foreach (DungeonGenerator.Room room in generator.Rooms)
        {
            if (skipStartRoom && room == generator.StartRoom) continue;
            if (skipExitRoom  && room == generator.ExitRoom)  continue;

            int count = Random.Range(minEnemiesPerRoom, maxEnemiesPerRoom + 1);
            if (count == 0) continue;

            roomsUsed++;
            for (int i = 0; i < count; i++)
            {
                Vector2Int gridPos = room.RandomInteriorPoint();
                Vector3 worldPos   = DungeonGenerator.GridToWorld(gridPos.x, gridPos.y, tileSize)
                                     + Vector3.up * spawnHeight;

                GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
                if (prefab == null) continue;

                Instantiate(prefab, worldPos, Quaternion.identity, enemyParent);
                totalSpawned++;
            }
        }

        Debug.Log($"[EnemySpawner] Spawned {totalSpawned} enemies across {roomsUsed} rooms.");
    }

    public void ClearEnemies()
    {
        if (enemyParent == null) return;
        for (int i = enemyParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(enemyParent.GetChild(i).gameObject);
    }
}
