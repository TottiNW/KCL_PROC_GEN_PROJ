using System.Collections.Generic;
using UnityEngine;

public class DungeonBuilder : MonoBehaviour
{
    [Header("Tile Prefabs")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject ceilingPrefab;
    [SerializeField] private GameObject exitPrefab;
    [SerializeField] private GameObject pillarPrefab;

    [Header("Settings")]
    [SerializeField] private float tileSize       = 4f;
    [SerializeField] private float wallHeight      = 4f;  // used for ceiling height
    [SerializeField] private float wallMeshHeight  = 5f;  // ACTUAL measured height of the wall mesh
    [SerializeField] private bool  spawnPillars    = true;

    [Header("References")]
    [SerializeField] private Transform dungeonRoot;

    public float TileSize => tileSize;

    // Tracks where pillars have already been placed so corners shared by
    // multiple walls only get one pillar.
    private HashSet<Vector3> placedPillars = new HashSet<Vector3>();

    public void BuildDungeon(int[,] map)
    {
        ClearDungeon();
        placedPillars.Clear();

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
                CheckAndSpawnWalls(map, row, col, worldPos, rows, cols);
            }
        }

        Debug.Log("[DungeonBuilder] Build complete.");
    }

    private void SpawnFloor(int tileType, Vector3 worldPos)
    {
        if (tileType == DungeonGenerator.EXIT)
        {
            Vector3 exitPos = worldPos + Vector3.up * 0.01f;
            Instantiate(exitPrefab, exitPos, Quaternion.identity, dungeonRoot);
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

            bool isOutOfBounds   = nr < 0 || nr >= rows || nc < 0 || nc >= cols;
            bool neighbourIsWall = isOutOfBounds || map[nr, nc] == DungeonGenerator.WALL;

            if (!neighbourIsWall) continue;

            float pushFactor = tileSize * 0.5f;
            Vector3 wallPos  = worldPos + new Vector3(
                dc * pushFactor,
                0f,   // anchors the wall bottom to the floor
                dr * pushFactor
            );

            Instantiate(wallPrefab, wallPos, Quaternion.Euler(0, yRot, 0), dungeonRoot);

            if (spawnPillars && pillarPrefab != null)
                SpawnCornerPillars(worldPos, dr, dc);
        }
    }

    private void SpawnCornerPillars(Vector3 worldPos, int dr, int dc)
    {
        float half = tileSize * 0.5f;

        // The two corners that sit at each end of this wall segment
        Vector3[] corners;
        if (dr != 0) // horizontal wall (north/south) → corners run east/west
        {
            corners = new[]
            {
                worldPos + new Vector3(-half, 0, dr * half),
                worldPos + new Vector3( half, 0, dr * half),
            };
        }
        else // vertical wall (east/west) → corners run north/south
        {
            corners = new[]
            {
                worldPos + new Vector3(dc * half, 0, -half),
                worldPos + new Vector3(dc * half, 0,  half),
            };
        }

        foreach (var c in corners)
        {
            // Round to avoid float-precision duplicates at shared corners
            Vector3 key = new Vector3(
                Mathf.Round(c.x * 10f) / 10f,
                0,
                Mathf.Round(c.z * 10f) / 10f
            );

            if (placedPillars.Contains(key)) continue;
            placedPillars.Add(key);

            Instantiate(pillarPrefab, c, Quaternion.identity, dungeonRoot);
        }
    }

    public void ClearDungeon()
    {
        for (int i = dungeonRoot.childCount - 1; i >= 0; i--)
            DestroyImmediate(dungeonRoot.GetChild(i).gameObject);
    }
}