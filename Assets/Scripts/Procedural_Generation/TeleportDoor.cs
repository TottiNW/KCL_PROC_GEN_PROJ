using UnityEngine;

/// <summary>
/// The exit portal. This script sits on the exit-door prefab (the one assigned to
/// DungeonBuilder.exitPrefab) and needs a Collider marked "Is Trigger" sized to the
/// doorway. A trigger collider doesn't physically block anything — it only reports when
/// something enters it. The player entering loads the next floor, once per door.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TeleportDoor : MonoBehaviour
{
    private FloorManager floorManager;
    // Latch to fire only once: walking through the trigger can raise OnTriggerEnter several
    // times, but a door should advance exactly one floor.
    private bool triggered;

    private void Start()
    {
        // Grab the scene's FloorManager to call on entry. Each floor rebuilds this door, so
        // the lookup happens fresh every time the door spawns.
        floorManager = FindObjectOfType<FloorManager>();
        if (floorManager == null)
            Debug.LogWarning("[TeleportDoor] No FloorManager found in scene — door will do nothing.");
    }

    // Unity calls this automatically whenever another collider enters the trigger.
    private void OnTriggerEnter(Collider other)
    {
        if (triggered || floorManager == null) return; // already used, or nothing to call
        if (!IsPlayer(other)) return;                   // ignore enemies, props, etc.

        triggered = true;
        floorManager.LoadNextFloor();
    }

    // Reports whether the entering collider is the player. The Cowsins player is built from
    // several nested objects, so the touching collider might be the object tagged "Player"
    // itself OR a child whose parent holds that tag — both cases are checked.
    private bool IsPlayer(Collider other)
    {
        if (other.CompareTag("Player")) return true;            // the collider itself is tagged
        Transform parent = other.transform.parent;
        return parent != null && parent.CompareTag("Player");   // or its parent is
    }
}
