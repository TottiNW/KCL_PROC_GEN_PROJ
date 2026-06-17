using System.Collections;
using UnityEngine;

public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private DungeonGenerator generator;
    [SerializeField] private DungeonBuilder   builder;
    [SerializeField] private NavMeshBaker     navMeshBaker;
    [SerializeField] private PlayerSpawner    playerSpawner;
    [SerializeField] private EnemySpawner     enemySpawner;

    private IEnumerator Start()
    {
        generator.GenerateMap();
        builder.BuildDungeon(generator.Map);

        // Wait one frame so the freshly instantiated floor colliders are
        // registered before the NavMesh bakes onto them.
        yield return null;

        navMeshBaker.Bake();

        Vector3 startCenter = generator.RoomCenterWorld(generator.StartRoom, builder.TileSize);
        playerSpawner.SpawnPlayer(startCenter);

        // Enemies spawn last, after the bake, so chase agents land on a valid NavMesh.
        enemySpawner.SpawnEnemies(generator, builder.TileSize);
    }
}