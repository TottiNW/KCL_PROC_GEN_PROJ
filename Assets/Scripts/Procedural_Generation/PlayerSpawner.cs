using UnityEngine;

// Teleports the player to a spot in the freshly built dungeon (the centre of the
// start room). Moving a player isn't as simple as setting its position, because the
// physics/movement components can fight or ignore a plain position change — so this
// picks the correct teleport technique based on which components the player has.
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;          // the player object to move (Cowsins' Player child)
    [SerializeField] private float spawnHeight = 1.5f;  // lift above the floor so the player doesn't sink into it

    public void SpawnPlayer(Vector3 startRoomCenter)
    {
        // Aim a little above the floor tile so the player lands on it, not inside it.
        Vector3 spawnPos = startRoomCenter + Vector3.up * spawnHeight;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Physics-driven player (has a Rigidbody). With interpolation on, the visible
            // position follows the physics position (rb.position), not transform.position,
            // so the teleport has to set rb.position. Velocities are zeroed first to stop
            // any leftover motion carrying over after the teleport.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = spawnPos;
        }
        else
        {
            // No Rigidbody, so the player moves via a CharacterController. A
            // CharacterController refuses direct position changes while enabled, so it gets
            // disabled, moved, then re-enabled.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = spawnPos;
            if (cc != null) cc.enabled = true;
        }

        Debug.Log($"[PlayerSpawner] Player moved to {spawnPos}");
    }
}