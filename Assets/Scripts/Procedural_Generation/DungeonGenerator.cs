using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    public const int WALL  = 0;
    public const int FLOOR = 1;
    public const int EXIT  = 2;

    [Header("Grid Size")]
    [Range(30, 100)] public int mapWidth  = 50;
    [Range(30, 100)] public int mapHeight = 50;

    [Header("BSP Settings")]
    [Range(2, 6)]  public int bspDepth         = 4;
    [Range(5, 15)] public int minPartitionSize  = 8;

    [Header("Room Settings")]
    [Range(3, 10)] public int minRoomSize  = 4;
    [Range(1, 3)]  public int roomPadding  = 1;

    [Header("Corridor Settings")]
    [Range(0.5f, 1f)] public float corridorBias        = 0.80f;
    [Range(0f, 0.6f)] public float corridorWidenChance = 0.35f;

    [Header("Seed")]
    public int seed = 0;

    public int[,]     Map       { get; private set; }
    public List<Room> Rooms     { get; private set; } = new List<Room>();
    public Room       StartRoom { get; private set; }
    public Room       ExitRoom  { get; private set; }

    // ── Room ──────────────────────────────────────────────────────────────────

    public class Room
    {
        public int X, Y, Width, Height;

        public Room(int x, int y, int w, int h)
        {
            X = x; Y = y; Width = w; Height = h;
        }

        public Vector2Int Center =>
            new Vector2Int(X + Width / 2, Y + Height / 2);

        public Vector2Int RandomInteriorPoint() =>
            new Vector2Int(Random.Range(X + 1, X + Width - 1),
                           Random.Range(Y + 1, Y + Height - 1));

        public bool Contains(int col, int row) =>
            col >= X && col < X + Width && row >= Y && row < Y + Height;
    }

    // ── BSP Node ──────────────────────────────────────────────────────────────

    private class BSPNode
    {
        public int X, Y, Width, Height;
        public BSPNode LeftChild, RightChild;
        public Room Room;

        public bool IsLeaf => LeftChild == null && RightChild == null;

        public BSPNode(int x, int y, int w, int h)
        {
            X = x; Y = y; Width = w; Height = h;
        }

        public void Split(int minSize, int depth)
        {
            if (depth <= 0) return;

            bool canSplitH = Height >= minSize * 2 + 2;
            bool canSplitV = Width  >= minSize * 2 + 2;

            if (!canSplitH && !canSplitV) return;

            bool splitHorizontally;
            if      (!canSplitH) splitHorizontally = false;
            else if (!canSplitV) splitHorizontally = true;
            else splitHorizontally = (Height > Width * 1.25f) ||
                                     (Mathf.Abs(Height - Width) < minSize && Random.value > 0.5f);

            if (splitHorizontally)
            {
                int splitMin = minSize;
                int splitMax = Height - minSize - 1;
                if (splitMin >= splitMax) return;

                int split = Random.Range(splitMin, splitMax);
                LeftChild  = new BSPNode(X, Y,         Width, split);
                RightChild = new BSPNode(X, Y + split, Width, Height - split);
            }
            else
            {
                int splitMin = minSize;
                int splitMax = Width - minSize - 1;
                if (splitMin >= splitMax) return;

                int split = Random.Range(splitMin, splitMax);
                LeftChild  = new BSPNode(X,         Y, split,         Height);
                RightChild = new BSPNode(X + split, Y, Width - split, Height);
            }

            LeftChild.Split(minSize,  depth - 1);
            RightChild.Split(minSize, depth - 1);
        }

        public void CarveRooms(int[,] map, int minRoomSize, int padding, List<Room> roomList)
        {
            if (!IsLeaf)
            {
                LeftChild.CarveRooms(map,  minRoomSize, padding, roomList);
                RightChild.CarveRooms(map, minRoomSize, padding, roomList);
                return;
            }

            int maxRoomW = Width  - padding * 2;
            int maxRoomH = Height - padding * 2;

            if (maxRoomW < minRoomSize || maxRoomH < minRoomSize) return;

            int roomW = Random.Range(minRoomSize, maxRoomW + 1);
            int roomH = Random.Range(minRoomSize, maxRoomH + 1);
            int roomX = X + padding + Random.Range(0, maxRoomW - roomW + 1);
            int roomY = Y + padding + Random.Range(0, maxRoomH - roomH + 1);

            roomX = Mathf.Clamp(roomX, 1, map.GetLength(1) - roomW - 1);
            roomY = Mathf.Clamp(roomY, 1, map.GetLength(0) - roomH - 1);

            for (int row = roomY; row < roomY + roomH; row++)
                for (int col = roomX; col < roomX + roomW; col++)
                    map[row, col] = FLOOR;

            Room = new Room(roomX, roomY, roomW, roomH);
            roomList.Add(Room);
        }

        public Room GetRoom()
        {
            if (Room != null) return Room;

            Room left  = LeftChild?.GetRoom();
            Room right = RightChild?.GetRoom();

            if (left  == null) return right;
            if (right == null) return left;

            return Random.value > 0.5f ? left : right;
        }

        public List<BSPNode> GetLeaves()
        {
            var result = new List<BSPNode>();
            if (IsLeaf) { result.Add(this); return result; }
            result.AddRange(LeftChild.GetLeaves());
            result.AddRange(RightChild.GetLeaves());
            return result;
        }
    }

    // ── Generation ────────────────────────────────────────────────────────────

    public void GenerateMap()
    {
        int activeSeed = seed == 0 ? System.Environment.TickCount : seed;
        Random.InitState(activeSeed);
        Debug.Log($"[DungeonGenerator] Seed: {activeSeed}");

        Map   = new int[mapHeight, mapWidth];
        Rooms = new List<Room>();

        for (int row = 0; row < mapHeight; row++)
            for (int col = 0; col < mapWidth; col++)
                Map[row, col] = WALL;

        var root = new BSPNode(1, 1, mapWidth - 2, mapHeight - 2);
        root.Split(minPartitionSize, bspDepth);
        root.CarveRooms(Map, minRoomSize, roomPadding, Rooms);

        if (Rooms.Count == 0)
        {
            Debug.LogError("[DungeonGenerator] No rooms carved. Try increasing map size or reducing minPartitionSize.");
            return;
        }

        ConnectSiblings(root);

        StartRoom = Rooms[0];
        ExitRoom  = FindFarthestRoom(StartRoom);

        var exitCenter = ExitRoom.Center;
        Map[exitCenter.y, exitCenter.x] = EXIT;

        Debug.Log($"[DungeonGenerator] Done — {Rooms.Count} rooms, Start: {StartRoom.Center}, Exit: {exitCenter}");
    }

    private void ConnectSiblings(BSPNode node)
    {
        if (node.IsLeaf) return;

        ConnectSiblings(node.LeftChild);
        ConnectSiblings(node.RightChild);

        Room roomA = node.LeftChild.GetRoom();
        Room roomB = node.RightChild.GetRoom();

        if (roomA != null && roomB != null)
            CarveRandomWalkCorridor(roomA, roomB);
    }

    private void CarveRandomWalkCorridor(Room roomA, Room roomB)
    {
        Vector2Int current = roomA.RandomInteriorPoint();
        Vector2Int target  = roomB.RandomInteriorPoint();

        int maxSteps = mapWidth * mapHeight;
        int steps    = 0;

        while (current != target && steps < maxSteps)
        {
            steps++;

            if (Random.value < corridorBias)
            {
                int dx = target.x - current.x;
                int dy = target.y - current.y;

                if      (dx == 0)                        current.y += (int)Mathf.Sign(dy);
                else if (dy == 0)                        current.x += (int)Mathf.Sign(dx);
                else if (Mathf.Abs(dx) > Mathf.Abs(dy)) current.x += (int)Mathf.Sign(dx);
                else                                     current.y += (int)Mathf.Sign(dy);
            }
            else
            {
                if (Random.value > 0.5f) current.x += Random.value > 0.5f ? 1 : -1;
                else                     current.y += Random.value > 0.5f ? 1 : -1;
            }

            current.x = Mathf.Clamp(current.x, 1, mapWidth  - 2);
            current.y = Mathf.Clamp(current.y, 1, mapHeight - 2);

            if (Map[current.y, current.x] != EXIT)
                Map[current.y, current.x] = FLOOR;

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

    public static Vector3 GridToWorld(int col, int row, float tileSize) =>
        new Vector3(col * tileSize, 0f, row * tileSize);

    public Vector3 RoomCenterWorld(Room room, float tileSize)
    {
        var c = room.Center;
        return GridToWorld(c.x, c.y, tileSize);
    }

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