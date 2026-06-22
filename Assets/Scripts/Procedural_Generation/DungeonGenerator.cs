using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Designs the dungeon LAYOUT as pure data — no 3D objects are created here (that's
/// DungeonBuilder's job). The result is a 2D grid (Map) where every cell is one of
/// three numbers: WALL, FLOOR or EXIT.
///
/// The layout uses BSP (Binary Space Partitioning): the whole map starts as one
/// rectangle and is repeatedly sliced into two smaller rectangles. Each final piece
/// gets one room carved into it, then corridors connect neighbouring rooms. A final
/// safety pass forces a path from the start room to every other room.
/// </summary>
public class DungeonGenerator : MonoBehaviour
{
    // The three kinds of cell. Plain ints keep the grid lightweight; the named constants
    // just keep the code readable (WALL instead of 0, etc.).
    public const int WALL  = 0;
    public const int FLOOR = 1;
    public const int EXIT  = 2;

    // Grid size in cells. [Range] shows these as sliders in the inspector.
    [Header("Grid Size")]
    [Range(30, 100)] public int mapWidth  = 50;
    [Range(30, 100)] public int mapHeight = 50;

    // BSP controls how the map is sliced up. Deeper = more slices = more (smaller) rooms.
    [Header("BSP Settings")]
    [Range(2, 6)]  public int bspDepth         = 4;  // how many times to keep slicing
    [Range(5, 15)] public int minPartitionSize  = 8; // never slice a piece smaller than this

    [Header("Room Settings")]
    [Range(3, 10)] public int minRoomSize  = 4;      // smallest room to carve
    [Range(1, 3)]  public int roomPadding  = 1;      // gap kept between a room and the edge of its slice

    // Corridors are dug with a "random walk": a point wanders from one room toward another,
    // mostly heading the right way but occasionally stepping sideways for a less straight look.
    [Header("Corridor Settings")]
    [Range(0.5f, 1f)] public float corridorBias        = 0.80f; // chance each step heads toward the target (higher = straighter)
    [Range(0f, 0.6f)] public float corridorWidenChance = 0.35f; // chance each step also widens the corridor

    // Random seed. The same seed reproduces the same dungeon every time, which helps when
    // debugging. 0 is special: it picks a fresh random seed each run.
    [Header("Seed")]
    public int seed = 0;

    [Header("Connectivity")]
    [Tooltip("Logs how many rooms were force-connected per generation.")]
    public bool verboseConnectivityLog = false;

    // The finished results. Readable by other scripts but only set here ("private set").
    public int[,]     Map       { get; private set; } // the grid of WALL/FLOOR/EXIT values
    public List<Room> Rooms     { get; private set; } = new List<Room>(); // every room that was carved
    public Room       StartRoom { get; private set; } // where the player spawns
    public Room       ExitRoom  { get; private set; } // where the exit portal goes

    // ── Room ──────────────────────────────────────────────────────────────────

    // A rectangle on the grid: bottom-left corner (X, Y) plus Width/Height.
    public class Room
    {
        public int X, Y, Width, Height;

        public Room(int x, int y, int w, int h)
        {
            X = x; Y = y; Width = w; Height = h;
        }

        // The middle cell of the room. ("=>" defines a read-only shortcut property.)
        public Vector2Int Center =>
            new Vector2Int(X + Width / 2, Y + Height / 2);

        // A random cell strictly INSIDE the room (the +1 / -1 keeps it off the walls).
        public Vector2Int RandomInteriorPoint() =>
            new Vector2Int(Random.Range(X + 1, X + Width - 1),
                           Random.Range(Y + 1, Y + Height - 1));

        // True when the given grid cell falls within this room's rectangle.
        public bool Contains(int col, int row) =>
            col >= X && col < X + Width && row >= Y && row < Y + Height;
    }

    // ── BSP Node ──────────────────────────────────────────────────────────────

    // One rectangle in the slicing process. Either it has two children (it was sliced into
    // a Left and Right half) or it's a "leaf" (never sliced) that holds one Room. All the
    // nodes together form a tree of rectangles.
    private class BSPNode
    {
        public int X, Y, Width, Height;
        public BSPNode LeftChild, RightChild;
        public Room Room;

        // A "leaf" is a node with no children — the smallest pieces, where rooms go.
        public bool IsLeaf => LeftChild == null && RightChild == null;

        public BSPNode(int x, int y, int w, int h)
        {
            X = x; Y = y; Width = w; Height = h;
        }

        // Slices this rectangle into two and recurses into each half (recursion = a method
        // that calls itself on smaller pieces). "depth" counts down so slicing stops after
        // bspDepth rounds.
        public void Split(int minSize, int depth)
        {
            if (depth <= 0) return; // out of slicing rounds

            // A split is only allowed if each half stays big enough on that axis
            // (room for two halves of minSize plus a little separation).
            bool canSplitH = Height >= minSize * 2 + 2; // can cut into top/bottom
            bool canSplitV = Width  >= minSize * 2 + 2; // can cut into left/right

            if (!canSplitH && !canSplitV) return; // too small to cut either way — stop here

            // Cut top/bottom (horizontal) or left/right (vertical). When only one axis
            // is possible, use it. Otherwise prefer cutting the long axis so pieces stay
            // squarish, with a coin flip when the piece is nearly square.
            bool splitHorizontally;
            if      (!canSplitH) splitHorizontally = false; // only vertical fits
            else if (!canSplitV) splitHorizontally = true;  // only horizontal fits
            else splitHorizontally = (Height > Width * 1.25f) ||
                                     (Mathf.Abs(Height - Width) < minSize && Random.value > 0.5f);

            if (splitHorizontally)
            {
                // Cut line somewhere down the height, keeping both halves >= minSize.
                int splitMin = minSize;
                int splitMax = Height - minSize - 1;
                if (splitMin >= splitMax) return; // no valid cut line — leave this node a leaf

                int split = Random.Range(splitMin, splitMax);
                LeftChild  = new BSPNode(X, Y,         Width, split);          // bottom piece
                RightChild = new BSPNode(X, Y + split, Width, Height - split); // top piece
            }
            else
            {
                // Same idea across the width, into a left and right piece.
                int splitMin = minSize;
                int splitMax = Width - minSize - 1;
                if (splitMin >= splitMax) return;

                int split = Random.Range(splitMin, splitMax);
                LeftChild  = new BSPNode(X,         Y, split,         Height); // left piece
                RightChild = new BSPNode(X + split, Y, Width - split, Height); // right piece
            }

            // Recurse into each new half with one fewer round left.
            LeftChild.Split(minSize,  depth - 1);
            RightChild.Split(minSize, depth - 1);
        }

        // Carves one room into each leaf piece. Non-leaf nodes pass the request down to
        // their children, so only the final (leaf) pieces get a room.
        public void CarveRooms(int[,] map, int minRoomSize, int padding, List<Room> roomList)
        {
            if (!IsLeaf)
            {
                LeftChild.CarveRooms(map,  minRoomSize, padding, roomList);
                RightChild.CarveRooms(map, minRoomSize, padding, roomList);
                return;
            }

            // The room can fill the piece minus the padding border on all sides.
            int maxRoomW = Width  - padding * 2;
            int maxRoomH = Height - padding * 2;

            if (maxRoomW < minRoomSize || maxRoomH < minRoomSize) return; // piece too small for a room

            // Random room size within the limits, placed at a random spot inside the piece.
            int roomW = Random.Range(minRoomSize, maxRoomW + 1);
            int roomH = Random.Range(minRoomSize, maxRoomH + 1);
            int roomX = X + padding + Random.Range(0, maxRoomW - roomW + 1);
            int roomY = Y + padding + Random.Range(0, maxRoomH - roomH + 1);

            // Clamp keeps the room fully inside the grid (forces a value into a range).
            roomX = Mathf.Clamp(roomX, 1, map.GetLength(1) - roomW - 1);
            roomY = Mathf.Clamp(roomY, 1, map.GetLength(0) - roomH - 1);

            // Carve = set every cell in the room's rectangle to FLOOR.
            for (int row = roomY; row < roomY + roomH; row++)
                for (int col = roomX; col < roomX + roomW; col++)
                    map[row, col] = FLOOR;

            Room = new Room(roomX, roomY, roomW, roomH);
            roomList.Add(Room);
        }

        // Returns a room belonging to this node. A leaf has its own; a parent returns one
        // from whichever child has one (at random when both do). ConnectSiblings uses this
        // to grab one room from each side of a split to join with a corridor.
        public Room GetRoom()
        {
            if (Room != null) return Room;

            // "?." calls only when the child isn't null, so this is safe on a missing child.
            Room left  = LeftChild?.GetRoom();
            Room right = RightChild?.GetRoom();

            if (left  == null) return right;
            if (right == null) return left;

            return Random.value > 0.5f ? left : right;
        }

        // Collects every leaf node beneath this one into a flat list. Not used in the
        // current generation flow; kept for inspecting all the smallest pieces.
        public List<BSPNode> GetLeaves()
        {
            var result = new List<BSPNode>();
            if (IsLeaf) { result.Add(this); return result; }
            result.AddRange(LeftChild.GetLeaves());
            result.AddRange(RightChild.GetLeaves());
            return result;
        }
    }

    // ── Config ────────────────────────────────────────────────────────────────

    /// <summary>Copies this floor's generation settings out of the config. The seed is
    /// deliberately NOT touched, so a fixed seed keeps producing the same dungeon. If no
    /// config is passed it does nothing, leaving the inspector values in place.</summary>
    public void ApplyConfig(FloorConfig config)
    {
        if (config == null) return;

        mapWidth           = config.mapWidth;
        mapHeight          = config.mapHeight;
        bspDepth           = config.bspDepth;
        minPartitionSize   = config.minPartitionSize;
        minRoomSize        = config.minRoomSize;
        roomPadding        = config.roomPadding;
        corridorBias       = config.corridorBias;
        corridorWidenChance = config.corridorWidenChance;
    }

    // ── Generation ────────────────────────────────────────────────────────────

    // Main entry point. Builds the whole layout from scratch and fills in Map, Rooms,
    // StartRoom and ExitRoom. Order: seed → blank grid → slice → carve rooms → connect
    // rooms → force connectivity → place the exit.
    public void GenerateMap()
    {
        // Set up the random number generator. A fixed seed gives a repeatable dungeon;
        // seed 0 falls back to the current tick count, so every run differs.
        int activeSeed = seed == 0 ? System.Environment.TickCount : seed;
        Random.InitState(activeSeed);
        Debug.Log($"[DungeonGenerator] Seed: {activeSeed}");

        // Fresh grid and empty room list. The grid is indexed [row, col] = [y, x], so it's
        // sized [height, width].
        Map   = new int[mapHeight, mapWidth];
        Rooms = new List<Room>();

        // Start fully solid; rooms and corridors are carved OUT of the wall below.
        for (int row = 0; row < mapHeight; row++)
            for (int col = 0; col < mapWidth; col++)
                Map[row, col] = WALL;

        // Build the BSP tree over the whole map (inset by 1 to leave a border wall), slice
        // it up, then carve a room into each leaf piece.
        var root = new BSPNode(1, 1, mapWidth - 2, mapHeight - 2);
        root.Split(minPartitionSize, bspDepth);
        root.CarveRooms(Map, minRoomSize, roomPadding, Rooms);

        if (Rooms.Count == 0)
        {
            Debug.LogError("[DungeonGenerator] No rooms carved. Try increasing map size or reducing minPartitionSize.");
            return;
        }

        // Dig corridors so neighbouring rooms in the tree are joined.
        ConnectSiblings(root);

        // First room carved is the player's start.
        StartRoom = Rooms[0];

        // The wandering corridors above can run out of steps before reaching a room and
        // leave it sealed off. This pass walks from the start and force-connects any
        // unreachable room. Runs BEFORE the exit is chosen so the exit is guaranteed
        // reachable. Uses no randomness, so it doesn't change the look of a given seed.
        EnsureFullConnectivity();

        // Exit goes in the room farthest from the start, forcing a trip across the dungeon.
        ExitRoom  = FindFarthestRoom(StartRoom);

        // Mark the exit room's centre cell as EXIT — that's where the portal spawns.
        var exitCenter = ExitRoom.Center;
        Map[exitCenter.y, exitCenter.x] = EXIT;

        Debug.Log($"[DungeonGenerator] Done — {Rooms.Count} rooms, Start: {StartRoom.Center}, Exit: {exitCenter}");
    }

    // Walks the BSP tree and, at every node that was split, digs a corridor between a room
    // on its left side and a room on its right side. Doing this at every level links the
    // two halves of every split.
    private void ConnectSiblings(BSPNode node)
    {
        if (node.IsLeaf) return; // a leaf has no two children to connect

        // Connect deeper levels first (recursion), then this node's two halves.
        ConnectSiblings(node.LeftChild);
        ConnectSiblings(node.RightChild);

        Room roomA = node.LeftChild.GetRoom();
        Room roomB = node.RightChild.GetRoom();

        if (roomA != null && roomB != null)
            CarveRandomWalkCorridor(roomA, roomB);
    }

    // Digs a corridor between two rooms with a "random walk": start at a point in room A and
    // step one cell at a time toward a point in room B. Most steps head toward the target
    // (corridorBias), but some go a random direction, so corridors wind a little instead of
    // running dead straight.
    private void CarveRandomWalkCorridor(Room roomA, Room roomB)
    {
        Vector2Int current = roomA.RandomInteriorPoint(); // current position of the walk
        Vector2Int target  = roomB.RandomInteriorPoint(); // destination point in room B

        // Safety limit so a stray walk can never loop forever.
        int maxSteps = mapWidth * mapHeight;
        int steps    = 0;

        while (current != target && steps < maxSteps)
        {
            steps++;

            if (Random.value < corridorBias)
            {
                // Biased step: move one cell toward the target along whichever axis is
                // farther off, closing the bigger gap first. Mathf.Sign gives +1 or -1
                // (the direction to step).
                int dx = target.x - current.x;
                int dy = target.y - current.y;

                if      (dx == 0)                        current.y += (int)Mathf.Sign(dy);
                else if (dy == 0)                        current.x += (int)Mathf.Sign(dx);
                else if (Mathf.Abs(dx) > Mathf.Abs(dy)) current.x += (int)Mathf.Sign(dx);
                else                                     current.y += (int)Mathf.Sign(dy);
            }
            else
            {
                // Random step: pick an axis, then step +1 or -1 on it. This is what adds
                // the wandering wiggle to the corridor.
                if (Random.value > 0.5f) current.x += Random.value > 0.5f ? 1 : -1;
                else                     current.y += Random.value > 0.5f ? 1 : -1;
            }

            // Keep the walk inside the grid border.
            current.x = Mathf.Clamp(current.x, 1, mapWidth  - 2);
            current.y = Mathf.Clamp(current.y, 1, mapHeight - 2);

            // Carve the landed-on cell, but never overwrite the EXIT marker.
            if (Map[current.y, current.x] != EXIT)
                Map[current.y, current.x] = FLOOR;

            // Occasionally carve a neighbouring cell too, widening the corridor past one
            // tile in places so it looks less like a thin maze.
            if (Random.value < corridorWidenChance)
            {
                int wx = Mathf.Clamp(current.x + (Random.value > 0.5f ? 1 : -1), 1, mapWidth  - 2);
                int wy = Mathf.Clamp(current.y + (Random.value > 0.5f ? 1 : -1), 1, mapHeight - 2);

                if (Map[wy, wx] != EXIT)
                    Map[wy, wx] = FLOOR;
            }
        }

        if (steps >= maxSteps)
            Debug.LogWarning($"[DungeonGenerator] Corridor hit step limit between {roomA.Center} and {roomB.Center}.");
    }

    // ── Connectivity enforcement ──────────────────────────────────────────────

    // Guarantees every room is reachable from the start. Flood-fills from the start (see
    // FloodFillFloor) to find everything currently reachable, collects any room left
    // stranded, and digs a straight L-shaped corridor from each stranded room to the
    // nearest reachable cell. Repeats because connecting one room can reveal others, and is
    // capped so it can never loop forever.
    private void EnsureFullConnectivity()
    {
        int maxPasses   = Rooms.Count + 1; // worst case, one pass per room
        int totalForced = 0;               // count of corridors that had to be forced

        for (int pass = 0; pass < maxPasses; pass++)
        {
            // Every cell currently reachable on foot from the start room's centre.
            bool[,] reachable = FloodFillFloor(StartRoom.Center);

            // Any room whose centre isn't in that reachable set is cut off.
            var isolated = new List<Room>();
            foreach (Room r in Rooms)
            {
                Vector2Int c = r.Center;
                if (!reachable[c.y, c.x]) isolated.Add(r);
            }

            if (isolated.Count == 0) // fully connected — done
            {
                if (verboseConnectivityLog)
                    Debug.Log($"[DungeonGenerator] Connectivity OK — {totalForced} room(s) force-connected.");
                return;
            }

            // Connect each stranded room to the nearest reachable cell.
            foreach (Room r in isolated)
            {
                if (TryFindNearestReachableCell(r.Center, reachable, out Vector2Int target))
                {
                    CarveLCorridor(r.Center, target);
                    totalForced++;
                }
            }
        }

        Debug.LogWarning($"[DungeonGenerator] Connectivity cap hit after {totalForced} " +
                         "force-connection(s) — a room may remain isolated.");
    }

    // Flood fill: starting from one cell, spreads out to every connected walkable cell, and
    // returns a grid of true/false marking what's reachable. Uses BFS (breadth-first search)
    // — a queue of cells to visit, expanding outward. The neighbour order is fixed so the
    // same seed always fills the same way.
    private bool[,] FloodFillFloor(Vector2Int start)
    {
        var visited = new bool[mapHeight, mapWidth]; // all false by default

        // The fill can't start from inside a wall or off the map.
        if (!InBounds(start.x, start.y) || Map[start.y, start.x] == WALL)
            return visited;

        var queue = new Queue<Vector2Int>(); // cells found but not yet expanded from
        visited[start.y, start.x] = true;
        queue.Enqueue(start);

        // The four directions to check from each cell: up, down, left, right.
        int[] dx = {  0, 0, -1, 1 };
        int[] dy = { -1, 1,  0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = cur.x + dx[i];
                int ny = cur.y + dy[i];
                // Skip anything off-map, already visited, or solid wall.
                if (!InBounds(nx, ny) || visited[ny, nx] || Map[ny, nx] == WALL) continue;

                visited[ny, nx] = true;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        return visited;
    }

    // Finds the reachable cell closest to `from`. "Manhattan" distance = horizontal steps
    // + vertical steps (no diagonals), which matches how corridors are dug. Scanning in a
    // fixed order and using strict "<" means ties always resolve the same way (repeatable).
    private bool TryFindNearestReachableCell(Vector2Int from, bool[,] reachable, out Vector2Int nearest)
    {
        nearest = default;
        int best  = int.MaxValue;
        bool found = false;

        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                if (!reachable[row, col]) continue;

                int dist = Mathf.Abs(col - from.x) + Mathf.Abs(row - from.y);
                if (dist < best)
                {
                    best    = dist;
                    nearest = new Vector2Int(col, row);
                    found   = true;
                }
            }
        }

        return found;
    }

    // Digs a simple L-shaped corridor: go straight across in X first, then straight up/down
    // in Y (or vice versa). One tile wide, to match the base corridors. Used by the
    // connectivity pass to forcibly link a stranded room.
    private void CarveLCorridor(Vector2Int from, Vector2Int to)
    {
        int x = from.x;
        int y = from.y;

        // Horizontal leg: step toward to.x, carving each cell along the way.
        int stepX = to.x >= x ? 1 : -1;
        while (x != to.x)
        {
            CarveFloor(x, y);
            x += stepX;
        }

        // Vertical leg: step toward to.y.
        int stepY = to.y >= y ? 1 : -1;
        while (y != to.y)
        {
            CarveFloor(x, y);
            y += stepY;
        }

        CarveFloor(to.x, to.y); // the final cell
    }

    // Turns one wall cell into floor. Leaves existing floor/exit cells alone.
    private void CarveFloor(int col, int row)
    {
        if (!InBounds(col, row)) return;
        if (Map[row, col] == WALL) Map[row, col] = FLOOR;
    }

    // True if (col, row) is a valid cell inside the grid.
    private bool InBounds(int col, int row) =>
        col >= 0 && col < mapWidth && row >= 0 && row < mapHeight;

    // Returns whichever room is physically farthest from `origin`, used to place the exit
    // far from the start. Compares SQUARED distance (dx*dx + dy*dy) rather than real
    // distance — same ordering, but skips the slower square-root step.
    private Room FindFarthestRoom(Room origin)
    {
        Room      farthest  = origin;
        float     maxDistSq = 0f;
        Vector2Int o        = origin.Center;

        foreach (Room r in Rooms)
        {
            if (r == origin) continue;
            Vector2Int c = r.Center;
            float distSq = (c.x - o.x) * (c.x - o.x) + (c.y - o.y) * (c.y - o.y);
            if (distSq > maxDistSq) { maxDistSq = distSq; farthest = r; }
        }

        return farthest;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Converts a grid cell (column, row) into a world-space position. Grid X maps to world
    // X, and grid Y (row) maps to world Z, since Y is "up" in Unity. "static" means it's
    // callable without a DungeonGenerator instance.
    public static Vector3 GridToWorld(int col, int row, float tileSize) =>
        new Vector3(col * tileSize, 0f, row * tileSize);

    // Convenience: the world-space position of a room's centre cell.
    public Vector3 RoomCenterWorld(Room room, float tileSize)
    {
        var c = room.Center;
        return GridToWorld(c.x, c.y, tileSize);
    }

    // Debug shortcut, exposed on the component's right-click menu via [ContextMenu]. In Play
    // Mode it rolls a fresh map and prints it to the Console — a quick way to check generator
    // settings without running the whole build/spawn pipeline.
    [ContextMenu("Test Generate (Runtime only)")]
    private void TestGenerate()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode first.");
            return;
        }
        GenerateMap();
        PrintMapToConsole();
    }

    // Prints the map to the Console as text art for a quick visual check: █ = wall, · = floor,
    // E = exit. Capped to 60x40 so a huge map doesn't flood the Console.
    private void PrintMapToConsole()
    {
        int printW = Mathf.Min(mapWidth,  60);
        int printH = Mathf.Min(mapHeight, 40);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DungeonGenerator] ASCII preview ({printW}x{printH}):");

        for (int row = 0; row < printH; row++)
        {
            for (int col = 0; col < printW; col++)
            {
                sb.Append(Map[row, col] switch
                {
                    WALL  => "█",
                    FLOOR => "·",
                    EXIT  => "E",
                    _     => "?"
                });
            }
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }
}