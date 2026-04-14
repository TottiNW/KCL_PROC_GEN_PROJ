using UnityEngine;

public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private DungeonGenerator generator;
    [SerializeField] private DungeonBuilder   builder;

    private void Start()
    {
        generator.GenerateMap();
        builder.BuildDungeon(generator.Map);
    }
}