using System.Collections.Generic;
using UnityEngine;

// Turns the generator's number grid into an actual 3D level. The generator only
// produces data (which cells are wall/floor/exit); this script walks that grid and
// spawns the real floor, wall, ceiling and exit prefabs in the right world positions.
public class DungeonBuilder : MonoBehaviour
{
    [Header("Tile Prefabs")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject ceilingPrefab;
    [SerializeField] private GameObject exitPrefab;
    [SerializeField] private GameObject pillarPrefab;

    [Header("Settings")]
    [SerializeField] private float tileSize       = 4f;  // size of one tile in world units (matches the art's 4-unit modules)
    [SerializeField] private float wallHeight      = 4f;  // how high to place the ceiling above the floor
    [SerializeField] private float wallMeshHeight  = 5f;  // the wall art's real height — kept separate from wallHeight since they can differ
    [SerializeField] private bool  spawnPillars    = true; // add pillars at wall corners for a nicer look

    [Header("References")]
    [SerializeField] private Transform dungeonRoot;       // empty object that all spawned tiles get parented under

    public float TileSize => tileSize;

    // Lets FloorManager read how many objects are currently under dungeonRoot — used by the
    // debug logs that confirm the old floor cleared before the new one builds.
    public int DungeonRootChildCount => dungeonRoot != null ? dungeonRoot.childCount : 0;

    // Records where a pillar has already been placed. Two neighbouring walls share a corner,
    // so without this the same spot would get two overlapping pillars. A HashSet gives a
    // fast "already placed here?" lookup.
    private HashSet<Vector3> placedPillars = new HashSet<Vector3>();

    /// <summary>Swaps in this floor's set of tile prefabs from its config. Each slot is
    /// only replaced if the config actually provides one, so a config can leave a slot
    /// empty to keep whatever was set in the inspector.</summary>
    public void ApplyConfig(FloorConfig config)
    {
        if (config == null) return;

        if (config.floorPrefab   != null) floorPrefab   = config.floorPrefab;
        if (config.wallPrefab    != null) wallPrefab    = config.wallPrefab;
        if (config.ceilingPrefab != null) ceilingPrefab = config.ceilingPrefab;
        if (config.exitPrefab    != null) exitPrefab    = config.exitPrefab;
        if (config.pillarPrefab  != null) pillarPrefab  = config.pillarPrefab;
    }

    // The main build step: takes the generator's grid (a 2D array of WALL/FLOOR/EXIT
    // numbers) and spawns the matching tiles. map[row, col] is the cell at that row/column.
    public void BuildDungeon(int[,] map)
    {
        ClearDungeon();        // remove the previous floor first
        placedPillars.Clear(); // forget last floor's pillar positions

        int rows = map.GetLength(0); // number of rows in the grid
        int cols = map.GetLength(1); // number of columns

        // Visit every cell in the grid, row by row.
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int tile = map[row, col];
                if (tile == DungeonGenerator.WALL) continue; // solid rock — nothing to spawn here

                // Convert the grid cell (col, row) into an actual world position.
                Vector3 worldPos = DungeonGenerator.GridToWorld(col, row, tileSize);

                SpawnFloor(tile, worldPos);   // floor (plus exit portal if this is the exit cell)
                SpawnCeiling(worldPos);       // ceiling above it
                CheckAndSpawnWalls(map, row, col, worldPos, rows, cols); // walls on any side facing rock
            }
        }

        Debug.Log("[DungeonBuilder] Build complete.");
    }

    // Spawns the floor tile. If this cell is the exit, it also drops the exit portal
    // prefab on top (nudged up 0.01 units so it doesn't z-fight / flicker with the floor).
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

    // Places a ceiling tile directly above the floor, wallHeight units up.
    private void SpawnCeiling(Vector3 worldPos)
    {
        Vector3 ceilPos = worldPos + Vector3.up * wallHeight;
        Instantiate(ceilingPrefab, ceilPos, Quaternion.identity, dungeonRoot);
    }

    // For a floor cell, looks at its 4 neighbours (up/down/left/right). Wherever the
    // neighbour is rock (or off the edge of the map), this cell needs a wall on that
    // side. Each entry below is (row offset, column offset, Y rotation) — the rotation
    // turns the wall to face the correct direction.
    private void CheckAndSpawnWalls(int[,] map, int row, int col,
                                    Vector3 worldPos, int rows, int cols)
    {
        var neighbours = new (int dr, int dc, float yRot)[]
        {
            (-1,  0,   0f),   // cell above  (north)
            ( 1,  0, 180f),   // cell below  (south)
            ( 0, -1,  90f),   // cell to the left  (west)
            ( 0,  1, 270f),   // cell to the right (east)
        };

        foreach (var (dr, dc, yRot) in neighbours)
        {
            int nr = row + dr; // neighbour row
            int nc = col + dc; // neighbour column

            // Anything past the grid edge counts as wall too, so the dungeon is sealed.
            bool isOutOfBounds   = nr < 0 || nr >= rows || nc < 0 || nc >= cols;
            bool neighbourIsWall = isOutOfBounds || map[nr, nc] == DungeonGenerator.WALL;

            if (!neighbourIsWall) continue; // neighbour is open floor — no wall needed on this side

            // Walls sit on the EDGE between two cells, so push half a tile toward the
            // neighbour. Y stays 0 so the wall's base sits on the floor.
            float pushFactor = tileSize * 0.5f;
            Vector3 wallPos  = worldPos + new Vector3(
                dc * pushFactor,
                0f,
                dr * pushFactor
            );

            Instantiate(wallPrefab, wallPos, Quaternion.Euler(0, yRot, 0), dungeonRoot);

            if (spawnPillars && pillarPrefab != null)
                SpawnCornerPillars(worldPos, dr, dc);
        }
    }

    // Puts a pillar at each end of a wall segment, to dress up the corners. Which way
    // the two corners lie depends on whether this is a horizontal or vertical wall.
    private void SpawnCornerPillars(Vector3 worldPos, int dr, int dc)
    {
        float half = tileSize * 0.5f;

        // Work out the two corner positions for this wall segment.
        Vector3[] corners;
        if (dr != 0) // wall runs along the north/south edge → its corners are to the east & west
        {
            corners = new[]
            {
                worldPos + new Vector3(-half, 0, dr * half),
                worldPos + new Vector3( half, 0, dr * half),
            };
        }
        else // wall runs along the east/west edge → its corners are to the north & south
        {
            corners = new[]
            {
                worldPos + new Vector3(dc * half, 0, -half),
                worldPos + new Vector3(dc * half, 0,  half),
            };
        }

        foreach (var c in corners)
        {
            // Neighbouring walls share corners, so the same spot comes up more than once.
            // Tiny floating-point differences would slip past a plain comparison, so the
            // position is rounded to 0.1 units to make a stable "key"; already-done corners
            // are then skipped.
            Vector3 key = new Vector3(
                Mathf.Round(c.x * 10f) / 10f,
                0,
                Mathf.Round(c.z * 10f) / 10f
            );

            if (placedPillars.Contains(key)) continue; // already a pillar here
            placedPillars.Add(key);

            Instantiate(pillarPrefab, c, Quaternion.identity, dungeonRoot);
        }
    }

    // Deletes the previous floor's tiles before a new one is built. Without this, each
    // new floor would be stacked on top of the old one.
    public void ClearDungeon()
    {
        if (dungeonRoot == null)
        {
            Debug.LogError("[DungeonBuilder] dungeonRoot is not assigned — cannot clear.");
            return;
        }

        int before = dungeonRoot.childCount;

        // DestroyImmediate deletes right now (not at end of frame) so the old floor is
        // fully gone before BuildDungeon starts spawning the new one in the same step.
        // Loop counts DOWN because the child list shrinks as items are deleted, and going
        // up would skip items. IMPORTANT: DestroyImmediate is NOT allowed inside a physics
        // trigger callback — that's why FloorManager waits a frame after the portal fires
        // before calling this (see LoadNextFloorDeferred there).
        for (int i = dungeonRoot.childCount - 1; i >= 0; i--)
            DestroyImmediate(dungeonRoot.GetChild(i).gameObject);

        Debug.Log($"[CLEAR-DEBUG] ClearDungeon: childCount before = {before}, after = {dungeonRoot.childCount}");
    }
}