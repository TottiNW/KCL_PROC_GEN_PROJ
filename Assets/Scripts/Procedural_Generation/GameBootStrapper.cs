using UnityEngine;

/// <summary>
/// The game's starting point. Unity calls Start() once when the scene begins, and
/// all this does is tell the FloorManager to build the very first floor. Everything
/// else (generating, building, spawning, and moving between floors) is handled by
/// FloorManager — this script just presses the "go" button once.
/// </summary>
public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private FloorManager floorManager;

    private void Start()
    {
        // LoadFloor is a coroutine (it pauses partway through to wait a frame), so it goes
        // through StartCoroutine rather than being called like a normal method.
        StartCoroutine(floorManager.LoadFloor());
    }
}
