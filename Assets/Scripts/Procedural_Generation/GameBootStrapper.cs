using UnityEngine;

public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private DungeonGenerator generator;
    [SerializeField] private DungeonBuilder   builder;
    [SerializeField] private PlayerSpawner    playerSpawner;

    private void Start()
    {
        generator.GenerateMap();
        builder.BuildDungeon(generator.Map);

        Vector3 startCenter = generator.RoomCenterWorld(generator.StartRoom, builder.TileSize);
        playerSpawner.SpawnPlayer(startCenter);
    }
}