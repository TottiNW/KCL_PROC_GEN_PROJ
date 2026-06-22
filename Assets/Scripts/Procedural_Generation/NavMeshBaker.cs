using UnityEngine;
using Unity.AI.Navigation;

public class NavMeshBaker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshSurface navMeshSurface;

    /// <summary>
    /// "Baking" a NavMesh means scanning the level geometry and pre-calculating the
    /// walkable surface enemies use to find their way around — without it, a NavMeshAgent
    /// has nowhere to walk. This rebuilds that surface for the current dungeon. It MUST run
    /// after DungeonBuilder has spawned the floor (a floor collider has to exist to bake
    /// onto), and it's safe to call once per floor: the old NavMesh is wiped first instead
    /// of a new one being layered on top.
    /// </summary>
    public void Bake()
    {
        if (navMeshSurface == null)
        {
            Debug.LogError("[NavMeshBaker ERROR] No NavMeshSurface assigned so cannot bake");
            return;
        }

        // Discard the previous floor's baked data first — otherwise each floor transition
        // would leave the old walkable area behind, overlapping the new one.
        if (navMeshSurface.navMeshData != null)
            navMeshSurface.RemoveData();

        // Do the actual scan-and-build of the walkable surface.
        navMeshSurface.BuildNavMesh();

        // After baking, navMeshData is filled in on success and stays null on failure.
        if (navMeshSurface.navMeshData != null)
            Debug.Log("[NavMeshBaker] NavMesh bake complete.");
        else
            Debug.LogError("[NavMeshBaker ERROR] NavMesh bake failed — navMeshData is null. " +
                           "Check for floor colliders exist or not and are on a bakeable layer.");
    }
}
