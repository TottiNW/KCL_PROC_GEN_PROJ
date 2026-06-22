using UnityEngine;
using UnityEngine.AI;
using cowsins;

/// <summary>
/// An enemy that hunts the player. Instead of rewriting health/damage/death from
/// scratch, it INHERITS from Cowsins' EnemyHealth (the ": EnemyHealth" below means
/// "is a kind of EnemyHealth"), so it automatically keeps all of Cowsins' damage,
/// death, UI and killfeed behaviour, and just adds the chasing on top. It moves using
/// a NavMeshAgent — Unity's pathfinding component that walks along the baked NavMesh —
/// so a NavMesh must already be baked before this enemy comes to life.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyChaseAI : EnemyHealth
{
    [Header("Chase AI")]
    [SerializeField] private float detectionRange = 15f;   // how close the player must be before the enemy notices
    [SerializeField] private float attackRange    = 2f;    // how close before it stops and swings
    [SerializeField] private float attackDamage   = 10f;   // damage per hit
    [SerializeField] private float attackCooldown = 1.5f;  // seconds to wait between hits
    [SerializeField] private float moveSpeed       = 3.5f; // chase movement speed

    private NavMeshAgent agent;
    private Transform    player;
    private PlayerStats  playerStats;
    private float        lastAttackTime;       // Time.time of the last hit, used for the cooldown

    // Start runs once when the enemy spawns. "override" + "base.Start()" runs EnemyHealth's
    // own setup first, then adds the chase setup on top, instead of replacing it.
    public override void Start()
    {
        base.Start();

        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;

        // Locate the player by its "Player" tag (set in the inspector on the player object).
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            // The PlayerStats component (which takes damage) might be on the tagged object
            // itself, on a parent, or on a child, depending on how the Cowsins player is
            // assembled. The ?? operator falls through to the next option when the current
            // one is null, so all three are tried and the first match is kept.
            playerStats = playerObj.GetComponent<PlayerStats>()
                          ?? playerObj.GetComponentInParent<PlayerStats>()
                          ?? playerObj.GetComponentInChildren<PlayerStats>();
        }

        if (player == null)
            Debug.LogWarning("[EnemyChaseAI] No object tagged 'Player' found — enemy will idle.");
        else if (playerStats == null)
            Debug.LogWarning("[EnemyChaseAI] Player found but no PlayerStats — enemy cannot deal damage.");
    }

    // Update runs every frame. This is the enemy's decision loop: idle, chase, or attack.
    private void Update()
    {
        if (isDead || player == null) return;   // nothing to do when dead or no player was found

        // The agent needs to be standing on the NavMesh to move. Right after spawning it
        // might not be (e.g. it appeared a frame before the bake finished, or landed on a
        // gap), and giving orders to an off-mesh agent throws errors — so wait until it's valid.
        if (agent == null || !agent.isOnNavMesh) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance > detectionRange)
        {
            // Player is too far away to notice — stand still.
            agent.isStopped = true;
            return;
        }

        if (distance <= attackRange)
        {
            // Close enough to hit: stop moving and attack (the cooldown is handled inside TryAttack).
            agent.isStopped = true;
            TryAttack();
        }
        else
        {
            // In between: walk toward the player. SetDestination asks the NavMeshAgent
            // to pathfind its own route there around walls and props.
            agent.isStopped = false;
            agent.SetDestination(player.position);
        }
    }

    // Deals damage, but only if enough time has passed since the last hit.
    private void TryAttack()
    {
        if (playerStats == null) return;
        if (Time.time - lastAttackTime < attackCooldown) return; // still cooling down

        lastAttackTime = Time.time;
        playerStats.Damage(attackDamage, false);
    }
}
