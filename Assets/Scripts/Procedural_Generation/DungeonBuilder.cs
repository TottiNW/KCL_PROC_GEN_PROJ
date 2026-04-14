using System.Collections.Generic;
using UnityEngine;

public class DungeonBuilder : MonoBehaviour
{
    [Header("Tile Prefabs")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject ceilingPrefab;
    [SerializeField] private GameObject exitPrefab;

    [Header("Settings")]
    [SerializeField] private float tileSize   = 3f;
    [SerializeField] private float wallHeight = 4f;

    [Header("References")]
    [SerializeField] private Transform dungeonRoot;

    // Expose tileSize so FloorManager and spawners can use it
    public float TileSize => tileSize;

    public void BuildDungeon(int[,] map)
    {
        ClearDungeon();

        int rows = map.GetLength(0);
        int cols = map.GetLength(1);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int tile = map[row, col];
                if (tile == DungeonGenerator.WALL) continue;

                Vector3 worldPos = DungeonGenerator.GridToWorld(col, row, tileSize);

                SpawnFloor(tile, worldPos);
                SpawnCeiling(worldPos);

                // For every non-wall tile, check its 4 neighbours.
                // If a neighbour is a wall, place a wall prefab on that edge.
                CheckAndSpawnWalls(map, row, col, worldPos, rows, cols);
            }
        }

        Debug.Log("[DungeonBuilder] Build complete.");
    }

    private void SpawnFloor(int tileType, Vector3 worldPos)
{
    if (tileType == DungeonGenerator.EXIT)
    {
        // Raise exit slightly above the floor tile to avoid Z-fighting
        Vector3 exitPos = worldPos + Vector3.up * 0.01f;
        Instantiate(exitPrefab, exitPos, Quaternion.identity, dungeonRoot);
        
        // Also spawn a regular floor tile underneath so there's no gap
        Instantiate(floorPrefab, worldPos, Quaternion.identity, dungeonRoot);
    }
    else
    {
        Instantiate(floorPrefab, worldPos, Quaternion.identity, dungeonRoot);
    }
}

    private void SpawnCeiling(Vector3 worldPos)
    {
        Vector3 ceilPos = worldPos + Vector3.up * wallHeight;
        Instantiate(ceilingPrefab, ceilPos, Quaternion.identity, dungeonRoot);
    }

    private void CheckAndSpawnWalls(int[,] map, int row, int col,
                                Vector3 worldPos, int rows, int cols)
{
    var neighbours = new (int dr, int dc, float yRot)[]
    {
        (-1,  0,   0f),
        ( 1,  0, 180f),
        ( 0, -1,  90f),
        ( 0,  1, 270f),
    };

    foreach (var (dr, dc, yRot) in neighbours)
    {
        int nr = row + dr;
        int nc = col + dc;

        bool isOutOfBounds    = nr < 0 || nr >= rows || nc < 0 || nc >= cols;
        bool neighbourIsWall  = isOutOfBounds || map[nr, nc] == DungeonGenerator.WALL;

        if (!neighbourIsWall) continue;

        // Push the wall fully into the wall tile's space rather than
        // sitting on the boundary, so it doesn't eat into the walkable area.
        float pushFactor = tileSize * 0.5f;
        Vector3 wallPos  = worldPos + new Vector3(
            dc * pushFactor,
            wallHeight * 0.5f,
            dr * pushFactor
        );

        Instantiate(wallPrefab, wallPos, Quaternion.Euler(0, yRot, 0), dungeonRoot);
    }
}

    public void ClearDungeon()
    {
        // Destroy all children of dungeonRoot so we start clean each floor
        for (int i = dungeonRoot.childCount - 1; i >= 0; i--)
            DestroyImmediate(dungeonRoot.GetChild(i).gameObject);
    }
}