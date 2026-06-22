using System.Collections.Generic;
using UnityEngine;

// Adds the "dressing" to a built dungeon: clutter props against walls, torches on walls,
// and weapon/loot tables in the start and exit rooms. It reads the same map the builder
// used and sprinkles decoration on top. This runs BEFORE the NavMesh is baked so that any
// solid props are taken into account, and enemies path around them.
public class DungeonDecorator : MonoBehaviour
{
    [Header("Prop Prefabs")]
    [SerializeField] private GameObject[] floorPropPrefabs;     // boxes, barrels, debris (sit on the floor)
    [SerializeField] private GameObject[] wallPropPrefabs;      // torches (mounted on walls)
    [SerializeField] private GameObject[] weaponPickupPrefabs;  // Cowsins weapon pickups

    // Each "chance" is a probability from 0 to 1 that's rolled per eligible cell.
    // 0.08 = roughly an 8% chance, so most cells stay empty and clutter stays sparse.
    [Header("Spawn Chances")]
    [Range(0, 1)] [SerializeField] private float floorPropChance = 0.08f;
    [Range(0, 1)] [SerializeField] private float torchChance     = 0.15f;

    [Header("Room Loot")]
    // Weapons live ONLY in the start and exit rooms, set out on tables. The exit
    // room is stocked like a Left 4 Dead safe room with extra ammo/health loot.
    [SerializeField] private GameObject[] tablePrefabs;          // Synty tables to place weapons on
    [SerializeField] private int startRoomWeapons = 2;
    [SerializeField] private int exitRoomWeapons  = 3;
    [SerializeField] private float tableTopHeight = 1f;          // height to sit loot on the table surface
    [SerializeField] private GameObject[] exitExtraLootPrefabs;  // ammo/health pickups for the safe room
    [SerializeField] private int exitRoomExtraLoot = 2;

    [Header("Corridor & Spacing")]
    // Minimum tile distance (Chebyshev) between any two torches, to thin out clusters.
    [SerializeField] private int torchMinSpacing = 3;
    // Narrow corridors get weird torch clusters; skip torches there by default.
    [SerializeField] private bool skipTorchesInCorridors = true;

    [Header("Height Offsets")]
    [SerializeField] private float floorPropHeight = 0f;
    [SerializeField] private float torchHeight     = 2.5f;

    [Header("Placement Tuning")]
    // How far to push a floor prop from the tile centre toward its adjacent wall,
    // so it hugs the wall instead of sitting stranded mid-tile.
    [SerializeField] private float floorPropWallOffset = 1.4f;
    // How far to pull a wall prop BACK from the wall plane toward the room, so flat
    // props sit flush on the room-facing surface instead of clipping into the wall.
    [SerializeField] private float wallPropInset = 0.3f;
    // Extra Y rotation (degrees) to correct a prop's facing axis (e.g. Synty banners
    // need their flat face pointing into the room).
    [SerializeField] private float wallPropYawOffset = 0f;
    // Multipliers applied to spawned props' localScale.
    [SerializeField] private Vector3 floorPropScale = Vector3.one;
    [SerializeField] private Vector3 wallPropScale  = Vector3.one;

    [Header("Filters")]
    [SerializeField] private bool skipStartRoom = true;
    [SerializeField] private bool skipExitRoom  = true;

    [Header("References")]
    [SerializeField] private Transform decorParent;

    // The four neighbouring directions (same offsets the builder uses for walls), each
    // paired with the rotation that turns a torch to face away from that wall, into the
    // room. dr/dc = row/column offset; faceYRot = Y rotation in degrees.
    private static readonly (int dr, int dc, float faceYRot)[] Neighbours =
    {
        (-1,  0,   0f),  // wall to the north → face south, into the room
        ( 1,  0, 180f),  // wall to the south → face north
        ( 0, -1,  90f),  // wall to the west  → face east
        ( 0,  1, 270f),  // wall to the east  → face west
    };

    // Remembers exact world spots where torches were placed, so a wall shared by two
    // floor cells doesn't get two overlapping torches.
    private HashSet<Vector3> placedTorches = new HashSet<Vector3>();

    // Records which grid cells have a torch, used to keep torches spaced apart
    // (see torchMinSpacing / IsTorchTooClose).
    private List<Vector2Int> placedTorchCells = new List<Vector2Int>();

    /// <summary>Copies this floor's loot counts in. When no config is applied the
    /// inspector defaults remain in effect.</summary>
    public void ApplyConfig(FloorConfig config)
    {
        if (config == null) return;

        startRoomWeapons = config.startRoomWeapons;
        exitRoomWeapons  = config.exitRoomWeapons;
    }

    // The main entry point, called by FloorManager after the floor is built. Walks every
    // floor cell and decides what (if anything) to place there, then adds room loot at the end.
    public void Decorate(DungeonGenerator generator, float tileSize)
    {
        if (generator == null || generator.Map == null)
        {
            Debug.LogWarning("[DungeonDecorator] No map to decorate — skipping.");
            return;
        }

        // Wipe last floor's decorations and reset the torch-tracking collections.
        ClearDecorations();
        placedTorches.Clear();
        placedTorchCells.Clear();

        int[,] map = generator.Map;
        int rows = map.GetLength(0);
        int cols = map.GetLength(1);

        int floorProps = 0; // running totals, just for the summary log at the end
        int torches    = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int tile = map[row, col];
                if (tile == DungeonGenerator.WALL) continue;

                if (IsInSkippedRoom(generator, col, row)) continue;

                Vector3 worldPos = DungeonGenerator.GridToWorld(col, row, tileSize);

                if (IsNarrowCorridor(map, row, col, rows, cols))
                {
                    // This is a 1-tile-wide passage. Don't drop floor props here — they'd
                    // block the only path through. Torches are optional in corridors.
                    if (!skipTorchesInCorridors)
                        torches += SpawnWallTorches(map, row, col, worldPos, tileSize, rows, cols);
                }
                else
                {
                    // Open floor: maybe a floor prop, plus maybe torches on any walls.
                    if (TrySpawnFloorProp(map, row, col, worldPos, rows, cols)) floorProps++;
                    torches += SpawnWallTorches(map, row, col, worldPos, tileSize, rows, cols);
                }
            }
        }

        int loot = PlaceRoomLoot(generator, tileSize);

        Debug.Log($"[DungeonDecorator] {floorProps} floor props, {torches} torches, " +
                  $"{loot} loot stations (start + exit rooms).");
    }

    // FLOOR PROPS — boxes/barrels/etc. Only placed against a wall, so corridors and open
    // floor centres stay walkable. Returns true if it actually spawned something.
    private bool TrySpawnFloorProp(int[,] map, int row, int col, Vector3 worldPos, int rows, int cols)
    {
        if (floorPropPrefabs == null || floorPropPrefabs.Length == 0) return false;
        if (Random.value >= floorPropChance)                         return false; // failed the random roll

        // Which neighbouring side is a wall to lean against? A corner touches several,
        // so this picks one of them at random (dr/dc is the direction of the chosen wall).
        if (!TryPickWallNeighbour(map, row, col, rows, cols, out int dr, out int dc))
            return false; // no adjacent wall — nothing to lean on

        GameObject prefab = floorPropPrefabs[Random.Range(0, floorPropPrefabs.Length)];
        if (prefab == null) return false;

        // Nudge the prop from the tile centre toward that wall so it sits against it
        // instead of floating in the middle of the tile. Random 90° turn for variety.
        Vector3 pos = worldPos
                      + new Vector3(dc, 0f, dr) * floorPropWallOffset
                      + Vector3.up * floorPropHeight;
        float yRot  = Random.Range(0, 4) * 90f;

        GameObject prop = Instantiate(prefab, pos, Quaternion.Euler(0f, yRot, 0f), decorParent);
        // Vector3.Scale multiplies each axis, applying floorPropScale on top of whatever
        // scale the prefab already had.
        prop.transform.localScale = Vector3.Scale(prop.transform.localScale, floorPropScale);
        return true;
    }

    // TORCHES — checks each wall touching this cell and may mount a torch on it, facing into
    // the room. Returns how many it placed (so the caller can total them up).
    private int SpawnWallTorches(int[,] map, int row, int col, Vector3 worldPos,
                                 float tileSize, int rows, int cols)
    {
        if (wallPropPrefabs == null || wallPropPrefabs.Length == 0) return 0;

        int spawned = 0;
        // How far out from the tile centre to put the torch. Half a tile reaches the wall;
        // subtracting wallPropInset pulls it back slightly so a flat torch sits on the
        // wall's surface instead of sinking into it.
        float push = tileSize * 0.5f - wallPropInset;

        foreach (var (dr, dc, faceYRot) in Neighbours)
        {
            int nr = row + dr;
            int nc = col + dc;

            bool isOutOfBounds   = nr < 0 || nr >= rows || nc < 0 || nc >= cols;
            bool neighbourIsWall = isOutOfBounds || map[nr, nc] == DungeonGenerator.WALL;
            if (!neighbourIsWall) continue;     // no wall on this side, no torch

            if (Random.value >= torchChance) continue; // failed the random roll

            // Keep torches from bunching up: skip if one is already within torchMinSpacing.
            if (IsTorchTooClose(col, row)) continue;

            Vector3 torchPos = worldPos + new Vector3(dc * push, torchHeight, dr * push);

            // Round the position to make a stable key, so two cells sharing this wall
            // don't both place a torch at (almost) the same spot.
            Vector3 key = new Vector3(
                Mathf.Round(torchPos.x * 10f) / 10f,
                Mathf.Round(torchPos.y * 10f) / 10f,
                Mathf.Round(torchPos.z * 10f) / 10f
            );
            if (placedTorches.Contains(key)) continue;
            placedTorches.Add(key);

            GameObject prefab = wallPropPrefabs[Random.Range(0, wallPropPrefabs.Length)];
            if (prefab == null) continue;

            GameObject prop = Instantiate(prefab, torchPos,
                Quaternion.Euler(0f, faceYRot + wallPropYawOffset, 0f), decorParent);
            prop.transform.localScale = Vector3.Scale(prop.transform.localScale, wallPropScale);
            placedTorchCells.Add(new Vector2Int(col, row));
            spawned++;
        }

        return spawned;
    }

    // ROOM LOOT — weapons appear ONLY in the start and exit rooms, laid out on tables. The
    // exit room is also stocked with extra ammo/health, like the "safe room" in Left 4 Dead.
    private int PlaceRoomLoot(DungeonGenerator generator, float tileSize)
    {
        if (tablePrefabs == null || tablePrefabs.Length == 0)
        {
            Debug.LogWarning("[DungeonDecorator] No table prefabs assigned — skipping room loot.");
            return 0;
        }

        int[,] map = generator.Map;
        int total = 0;

        // START ROOM — just weapon tables. The room centre is passed as a "skip" cell because
        // that's exactly where the player spawns; a table there would land on top of them.
        var startUsed = new HashSet<Vector2Int>();
        Vector2Int startSpawn = generator.StartRoom != null ? generator.StartRoom.Center
                                                            : new Vector2Int(-1, -1);
        total += PlaceLootStations(generator.StartRoom, map, tileSize,
                                   startRoomWeapons, weaponPickupPrefabs, startUsed, startSpawn);

        // EXIT ROOM — weapon tables PLUS extra ammo/health. Both calls share ONE used-cell
        // set (exitUsed) so a weapon table and a loot table never land on the same spot.
        // (The exit door cell is already protected by the EXIT check inside PlaceLootStations.)
        var exitUsed = new HashSet<Vector2Int>();
        Vector2Int noSkip = new Vector2Int(-1, -1); // (-1,-1) is off-grid, so "skip nothing"
        total += PlaceLootStations(generator.ExitRoom, map, tileSize,
                                   exitRoomWeapons, weaponPickupPrefabs, exitUsed, noSkip);
        total += PlaceLootStations(generator.ExitRoom, map, tileSize,
                                   exitRoomExtraLoot, exitExtraLootPrefabs, exitUsed, noSkip);

        return total;
    }

    // Places up to `count` loot stations in `room`. Each station = a random table on the
    // floor with a random item from `lootPrefabs` sitting on top. It keeps trying random
    // interior cells, skipping the exit-door cell, the `skipCell` (e.g. player spawn), and
    // any cell already used. `out`-style retries are bounded so it can't loop forever.
    private int PlaceLootStations(DungeonGenerator.Room room, int[,] map, float tileSize,
                                  int count, GameObject[] lootPrefabs, HashSet<Vector2Int> usedCells,
                                  Vector2Int skipCell)
    {
        if (room == null)                                     return 0;
        if (count <= 0)                                       return 0;
        if (lootPrefabs == null || lootPrefabs.Length == 0)   return 0;

        int placed   = 0;
        int attempts = 0;
        int maxAttempts = count * 20; // give up after this many tries (prevents an endless loop)

        while (placed < count && attempts < maxAttempts)
        {
            attempts++;

            Vector2Int cell = room.RandomInteriorPoint();

            // Reject the cell if it's the exit door, the protected skipCell, or already
            // taken. usedCells.Add returns false when the cell was already in the set.
            if (map[cell.y, cell.x] == DungeonGenerator.EXIT) continue;
            if (cell == skipCell) continue;
            if (!usedCells.Add(cell)) continue;

            Vector3 floorPos = DungeonGenerator.GridToWorld(cell.x, cell.y, tileSize);

            // The table on the floor (random rotation in 90° steps).
            GameObject table = tablePrefabs[Random.Range(0, tablePrefabs.Length)];
            if (table != null)
                Instantiate(table, floorPos,
                    Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f), decorParent);

            // The item sitting on top, raised by tableTopHeight to rest on the table surface.
            GameObject loot = lootPrefabs[Random.Range(0, lootPrefabs.Length)];
            if (loot != null)
                Instantiate(loot, floorPos + Vector3.up * tableTopHeight,
                    Quaternion.identity, decorParent);

            placed++;
        }

        return placed;
    }

    // Picks one wall-touching neighbour direction for the given cell. If the cell touches
    // several walls (a corner), it chooses one of them with equal odds, then returns its
    // offset via dr/dc. Returns false if no neighbour is a wall.
    private bool TryPickWallNeighbour(int[,] map, int row, int col, int rows, int cols,
                                      out int dr, out int dc)
    {
        dr = 0; dc = 0;
        int found = 0;

        foreach (var (ndr, ndc, _) in Neighbours)
        {
            int nr = row + ndr;
            int nc = col + ndc;
            bool isOutOfBounds = nr < 0 || nr >= rows || nc < 0 || nc >= cols;
            if (!(isOutOfBounds || map[nr, nc] == DungeonGenerator.WALL)) continue;

            found++;
            // "Reservoir sampling" picks one item fairly from a stream without knowing how
            // many there'll be: the Nth wall found has a 1-in-N chance of replacing the pick,
            // which works out to every wall being equally likely in the end.
            if (Random.Range(0, found) == 0)
            {
                dr = ndr;
                dc = ndc;
            }
        }

        return found > 0;
    }

    // True when the cell has walls on two OPPOSITE sides (north+south, or east+west) — that
    // means it's a straight, one-tile-wide passage where blocking props shouldn't go.
    private bool IsNarrowCorridor(int[,] map, int row, int col, int rows, int cols)
    {
        bool north = IsWallAt(map, row - 1, col, rows, cols);
        bool south = IsWallAt(map, row + 1, col, rows, cols);
        bool east  = IsWallAt(map, row, col + 1, rows, cols);
        bool west  = IsWallAt(map, row, col - 1, rows, cols);

        return (north && south) || (east && west);
    }

    // Helper for IsNarrowCorridor: is this cell a wall? Off-the-map counts as wall too,
    // matching how the builder treats edges.
    private bool IsWallAt(int[,] map, int row, int col, int rows, int cols)
    {
        bool isOutOfBounds = row < 0 || row >= rows || col < 0 || col >= cols;
        return isOutOfBounds || map[row, col] == DungeonGenerator.WALL;
    }

    // True if another torch is already closer than torchMinSpacing tiles away, so this one
    // can be skipped to avoid clusters. Uses Chebyshev distance — max(dx, dy) — which treats
    // one diagonal step the same as one straight step.
    private bool IsTorchTooClose(int col, int row)
    {
        if (torchMinSpacing <= 0) return false; // spacing disabled

        foreach (Vector2Int cell in placedTorchCells)
        {
            int dx = Mathf.Abs(cell.x - col);
            int dy = Mathf.Abs(cell.y - row);
            if (Mathf.Max(dx, dy) < torchMinSpacing) return true; // too close
        }
        return false;
    }

    // True if this cell is in a room marked to leave undecorated (start and/or exit), so the
    // main loop skips it. Room loot is added to those rooms separately.
    private bool IsInSkippedRoom(DungeonGenerator generator, int col, int row)
    {
        if (skipStartRoom && generator.StartRoom != null && generator.StartRoom.Contains(col, row))
            return true;
        if (skipExitRoom && generator.ExitRoom != null && generator.ExitRoom.Contains(col, row))
            return true;
        return false;
    }

    // Deletes all decorations from the previous floor (same pattern as the builder/spawner:
    // loop backwards and DestroyImmediate, so nothing carries over to the next floor).
    public void ClearDecorations()
    {
        if (decorParent == null) return;
        for (int i = decorParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(decorParent.GetChild(i).gameObject);
    }
}
