using UnityEngine;

/// <summary>
/// A "recipe" for one floor, saved as a reusable asset file in the Project (not a
/// scene object). A ScriptableObject is Unity's way of storing data as an asset, so
/// several of these can be created in the editor (right-click → Create → ProcGen →
/// Floor Config), each holding one floor's size, look, enemies and loot — no code
/// changes needed. At runtime FloorManager grabs the right config for the current floor
/// and passes it to each system's ApplyConfig method, where each one copies out only the
/// fields it needs.
/// </summary>
[CreateAssetMenu(menuName = "ProcGen/Floor Config", fileName = "FloorConfig")]
public class FloorConfig : ScriptableObject
{
    // How the layout is generated. [Range] shows these as sliders in the inspector and
    // limits the values to the safe min/max.
    [Header("Generation")]
    [Range(30, 100)] public int mapWidth  = 50;             // grid size in tiles
    [Range(30, 100)] public int mapHeight = 50;
    [Range(2, 6)]    public int bspDepth         = 4;        // how many times the map is subdivided (more = smaller rooms)
    [Range(5, 15)]   public int minPartitionSize = 8;        // smallest a subdivided chunk may get
    [Range(3, 10)]   public int minRoomSize      = 4;        // smallest a carved room may be
    [Range(1, 3)]    public int roomPadding       = 1;       // empty border kept between a room and its chunk edge
    [Range(0, 1)]    public float corridorBias        = 0.80f; // how strongly corridors head toward their target (1 = straight)
    [Range(0, 1)]    public float corridorWidenChance = 0.35f; // chance per step a corridor thickens out

    // Which prefabs to build the floor from. Leaving a slot empty tells the builder
    // to keep whatever default it already has, so a config can swap just the walls.
    [Header("Tileset")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject ceilingPrefab;
    public GameObject exitPrefab;
    public GameObject pillarPrefab;

    [Header("Enemies")]
    public int enemiesPerRoom = 2;                 // most enemies that can spawn in one room
    public GameObject[] enemyPrefabs;              // which enemy types are allowed on this floor
    public float enemyHealthMultiplier = 1f;       // 1 = normal health; 2 = double, etc.

    [Header("Loot")]
    public int startRoomWeapons = 2;               // weapon tables placed in the start room
    public int exitRoomWeapons  = 3;               // weapon tables placed in the exit (safe) room
}
