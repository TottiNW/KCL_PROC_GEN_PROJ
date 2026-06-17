using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;          // CowsinsFPSController
    [SerializeField] private float spawnHeight = 1.5f;  // lift so it doesn't clip the floor

    public void SpawnPlayer(Vector3 startRoomCenter)
    {
        Vector3 spawnPos = startRoomCenter + Vector3.up * spawnHeight;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // With Rigidbody interpolation, visuals track rb.position (physics state),
            // not transform.position, so we must use rb.position to teleport correctly.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = spawnPos;
        }
        else
        {
            // Fallback: CharacterController fights direct transform moves, so disable it first
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = spawnPos;
            if (cc != null) cc.enabled = true;
        }

        Debug.Log($"[PlayerSpawner] Player moved to {spawnPos}");
    }
}