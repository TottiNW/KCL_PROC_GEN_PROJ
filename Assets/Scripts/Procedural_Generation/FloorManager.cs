using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "conductor" of the whole dungeon. On its own it doesn't generate or build
/// anything — it just calls the other systems in the right order: generate the layout
/// → build the geometry → decorate → bake the NavMesh → spawn the player → spawn enemies.
/// It runs that sequence once at startup and again every time the player takes the exit
/// portal (which calls <see cref="LoadNextFloor"/>), so this one script is the single
/// place that knows how a floor is assembled.
/// </summary>
public class FloorManager : MonoBehaviour
{
    [Header("Pipeline References")]
    [SerializeField] private DungeonGenerator generator;
    [SerializeField] private DungeonBuilder   builder;
    [SerializeField] private NavMeshBaker     navMeshBaker;
    [SerializeField] private PlayerSpawner    playerSpawner;
    [SerializeField] private EnemySpawner     enemySpawner;
    [SerializeField] private DungeonDecorator decorator;

    [Header("Floor Configs")]
    [Tooltip("One config per floor. Floor 1 → element 0. Floors past the list reuse " +
             "the last element with a difficulty ramp on top. Empty = inspector defaults.")]
    [SerializeField] private List<FloorConfig> floorConfigs = new List<FloorConfig>();
    [Tooltip("Extra enemies-per-room added for each floor descended past the configured list.")]
    [SerializeField] private int extraEnemiesPerFloorBeyondList = 1;
    [Tooltip("Extra enemy health multiplier added per floor descended past the configured list.")]
    [SerializeField] private float extraHealthPerFloorBeyondList = 0.15f;

    public int CurrentFloor { get; private set; } = 1; // 1 = first floor; counts up each portal

    // A simple "busy" flag. Building a floor takes more than one frame, and two builds must
    // never run at once (e.g. the player brushing the portal twice). While this is true,
    // LoadNextFloor/ReloadCurrentFloor ignore new requests.
    private bool isLoading;

    // A coroutine (note the IEnumerator return type and the "yield" lines below): a method
    // that can pause partway through and resume next frame. That pause is needed to wait a
    // frame after building before the NavMesh can bake. GameBootstrapper starts it for floor
    // 1; LoadNextFloorDeferred runs it for later floors.
    public IEnumerator LoadFloor()
    {
        // Mark busy. (On the portal path LoadNextFloor already set this true; setting it
        // again is harmless and also covers the startup path.)
        isLoading = true;

        // a. Clear whatever the previous floor left behind. Guard nulls — the very
        //    first load has nothing to clear, and the decorator is optional.
        if (builder != null)      builder.ClearDungeon();
        if (enemySpawner != null) enemySpawner.ClearEnemies();
        if (decorator != null)    decorator.ClearDecorations();

        // a2. Pick and apply this floor's config BEFORE generating, so each system
        //     generates/builds/spawns from the right settings.
        ApplyFloorConfig();

        // b. Generate the map data.
        generator.GenerateMap();

        // c. Build the geometry from the map.
        int childBefore = builder.DungeonRootChildCount;
        builder.BuildDungeon(generator.Map);
        int childAfter = builder.DungeonRootChildCount;
        Debug.Log($"[CLEAR-DEBUG] floor {CurrentFloor}: childCount before build = {childBefore}, after build = {childAfter}");

        // d. Decorate (props/torches/loot) before the bake so solid prop colliders
        //    are included when the NavMesh bakes and agents path around them.
        if (decorator != null)
            decorator.Decorate(generator, builder.TileSize);

        // e. Wait one frame so the freshly instantiated colliders register before baking.
        yield return null;

        // f. Bake the NavMesh onto the new floor.
        navMeshBaker.Bake();

        // g. Spawn the player at the start room centre. PlayerSpawner re-applies the
        //    position past Cowsins' init reset, so call it as-is.
        Vector3 startCenter = generator.RoomCenterWorld(generator.StartRoom, builder.TileSize);
        playerSpawner.SpawnPlayer(startCenter);

        // h. Enemies last, after the bake, so chase agents land on a valid NavMesh.
        enemySpawner.SpawnEnemies(generator, builder.TileSize);

        Debug.Log($"[FloorManager] Floor {CurrentFloor} loaded.");

        isLoading = false;
    }

    // Picks the FloorConfig for the current floor and passes it to each system. The list is
    // 0-based but floors count from 1, so floor 1 uses floorConfigs[0]. Floors DEEPER than
    // the list has entries reuse the last config but get gradually harder (more enemies /
    // more health) the further past the list they are. That harder version is made on a
    // temporary COPY so the saved config asset on disk is never changed.
    private void ApplyFloorConfig()
    {
        if (floorConfigs == null || floorConfigs.Count == 0)
            return; // no configs set → leave each system on its inspector defaults

        // Clamp the index to the last entry, so floors beyond the list reuse the last config.
        int lastIndex = floorConfigs.Count - 1;
        int index     = Mathf.Min(CurrentFloor - 1, lastIndex);
        FloorConfig cfg = floorConfigs[index];

        if (cfg == null)
        {
            Debug.LogWarning($"[FloorManager] floorConfigs[{index}] is null — using inspector defaults.");
            return;
        }

        // How many floors PAST the end of the list this is. 0 (or less) is an exact match;
        // greater than 0 means the last config is being reused and difficulty should ramp.
        int floorsPastList = (CurrentFloor - 1) - lastIndex;

        FloorConfig applied = cfg;   // default: use the config asset as-is
        bool isClone = false;
        if (floorsPastList > 0 &&
            (extraEnemiesPerFloorBeyondList != 0 || extraHealthPerFloorBeyondList != 0f))
        {
            // Instantiate() makes an in-memory copy of the asset that's safe to tweak.
            applied = Instantiate(cfg);
            applied.enemiesPerRoom        += extraEnemiesPerFloorBeyondList * floorsPastList;
            applied.enemyHealthMultiplier += extraHealthPerFloorBeyondList * floorsPastList;
            isClone = true;
        }

        // Hand the chosen config to each system; each copies out only the parts it needs.
        if (generator != null)    generator.ApplyConfig(applied);
        if (builder != null)      builder.ApplyConfig(applied);
        if (enemySpawner != null) enemySpawner.ApplyConfig(applied);
        if (decorator != null)    decorator.ApplyConfig(applied);

        // The systems read their values right away, so a temporary copy can be destroyed
        // now to avoid leaking copies into memory.
        if (isClone) Destroy(applied);

        if (floorsPastList > 0)
            Debug.Log($"[FloorManager] Floor {CurrentFloor}: reusing '{cfg.name}' " +
                      $"+{floorsPastList} difficulty step(s).");
    }

    // Called by the exit portal (TeleportDoor). Advances to the next floor number and
    // rebuilds. The isLoading check makes walking through the portal twice a no-op.
    public void LoadNextFloor()
    {
        if (isLoading) return; // already building a floor; ignore the extra trigger
        isLoading = true;      // set busy NOW so nothing sneaks in during the one-frame wait
        CurrentFloor++;
        StartCoroutine(LoadNextFloorDeferred());
    }

    /// <summary>
    /// Re-rolls the CURRENT floor without advancing <see cref="CurrentFloor"/> — same
    /// deferred LoadFloor path as a portal transition, just without the increment. Useful
    /// for testing generation variety and the connectivity repair on a fixed floor number.
    /// Honours the same <c>isLoading</c> guard so it can't overlap an in-flight transition.
    /// </summary>
    public void ReloadCurrentFloor()
    {
        if (isLoading) return; // already mid-transition; ignore the reload request
        isLoading = true;      // latch immediately so the one-frame defer can't re-enter
        StartCoroutine(LoadNextFloorDeferred());
    }

    // Why this exists: the portal triggers LoadNextFloor from inside Unity's physics
    // collision callback (OnTriggerEnter). A coroutine starts running immediately, up to its
    // first "yield", so calling LoadFloor directly would run ClearDungeon()'s DestroyImmediate
    // WHILE physics is still resolving — which Unity forbids, leaving the old floor uncleared.
    // The "yield return null" waits one frame first, leaving the physics callback for a normal
    // frame where deleting is allowed, and only THEN runs the real LoadFloor sequence.
    private IEnumerator LoadNextFloorDeferred()
    {
        yield return null;       // wait one frame (leave the physics callback)
        yield return LoadFloor(); // now run the full build sequence
    }
}
