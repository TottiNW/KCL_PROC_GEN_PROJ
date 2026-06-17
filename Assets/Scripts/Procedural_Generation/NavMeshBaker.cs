using UnityEngine;
using Unity.AI.Navigation;

public class NavMeshBaker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshSurface navMeshSurface;

    /// <summary>
    /// Bakes the NavMesh for the freshly built dungeon. Must run AFTER the
    /// DungeonBuilder has instantiated floor colliders, otherwise there is
    /// nothing to bake onto.
    /// </summary>
    public void Bake()
    {
        if (navMeshSurface == null)
        {
            Debug.LogError("[NavMeshBaker ERROR] No NavMeshSurface assigned so cannot bake");
            return;
        }

        navMeshSurface.BuildNavMesh();

        if (navMeshSurface.navMeshData != null)
            Debug.Log("[NavMeshBaker] NavMesh bake complete.");
        else
            Debug.LogError("[NavMeshBaker ERROR] NavMesh bake failed — navMeshData is null. " +
                           "Check for floor colliders exist or not and are on a bakeable layer.");
    }
}
