using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;          // the CowsinsFPSController
    [SerializeField] private float spawnHeight = 1.5f;  // lift so they don't clip the floor

    public void SpawnPlayer(Vector3 startRoomCenter)
    {
        Vector3 spawnPos = startRoomCenter + Vector3.up * spawnHeight;

        // CharacterController fights direct transform moves, so disable it first
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        player.position = spawnPos;

        if (cc != null) cc.enabled = true;

        Debug.Log($"[PlayerSpawner] Player moved to {spawnPos}");
        Debug.Log($"[PlayerSpawner] Player WORLD pos is now {player.position}");
    }
}