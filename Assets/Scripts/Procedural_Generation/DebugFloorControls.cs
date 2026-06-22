using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor-only debug helper: skip floor progression from the keyboard to test
/// floor advancement and generation without walking to the portal.
/// </summary>
public class DebugFloorControls : MonoBehaviour
{
    [Tooltip("FloorManager to drive. Auto-found via FindFirstObjectByType in Awake if left unassigned.")]
    [SerializeField] private FloorManager floorManager;

    [Tooltip("Advance to the next floor (same as taking the portal).")]
    [SerializeField] private Key nextFloorKey = Key.N;

    [Tooltip("Re-roll the current floor without advancing CurrentFloor.")]
    [SerializeField] private Key reloadFloorKey = Key.R;

    // Awake runs once when the object loads, before the game starts.
    // When the FloorManager slot is empty, this finds the one in the scene automatically.
    private void Awake()
    {
        if (floorManager == null)
            floorManager = FindFirstObjectByType<FloorManager>();
    }

// Everything between #if UNITY_EDITOR and #endif is ONLY compiled when running inside the
// Unity Editor. In a real built game this code simply doesn't exist, so these debug keys
// can never affect players.
#if UNITY_EDITOR
    // Update runs every frame; here it just watches the keyboard. Uses Unity's new Input
    // System (Keyboard.current), the same one the Cowsins controller relies on.
    private void Update()
    {
        if (Keyboard.current == null) return; // no keyboard connected this frame
        if (floorManager == null)     return; // nothing to control

        // wasPressedThisFrame is true only on the single frame the key goes down, so holding
        // the key doesn't fire repeatedly. The FloorManager calls below already ignore the
        // press if a floor is mid-build (its isLoading guard), so mashing keys is safe.
        if (Keyboard.current[nextFloorKey].wasPressedThisFrame)
        {
            Debug.Log("[DEBUG] skip to next floor");
            floorManager.LoadNextFloor();      // advance one floor, same as the portal
        }
        else if (Keyboard.current[reloadFloorKey].wasPressedThisFrame)
        {
            Debug.Log("[DEBUG] reload current floor");
            floorManager.ReloadCurrentFloor(); // re-roll the same floor number
        }
    }
#endif
}
