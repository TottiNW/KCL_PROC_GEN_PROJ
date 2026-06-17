using UnityEngine;
using UnityEngine.AI;
using cowsins;

/// <summary>
/// A chasing enemy that reuses Cowsins' EnemyHealth (so it keeps damage, death,
/// UI and killfeed behaviour) and adds NavMeshAgent-driven pursuit of the player.
/// Requires a baked NavMesh to exist before it spawns/enables.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyChaseAI : EnemyHealth
{
    [Header("Chase AI")]
    [SerializeField] private float detectionRange = 15f;
    [SerializeField] private float attackRange    = 2f;
    [SerializeField] private float attackDamage   = 10f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float moveSpeed       = 3.5f;

    private NavMeshAgent agent;
    private Transform    player;
    private PlayerStats  playerStats;
    private float        lastAttackTime;

    public override void Start()
    {
        base.Start();

        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            // PlayerStats can live as component on the tagged object, a parent,
            // or a child, so resolve it from all three just in case
            playerStats = playerObj.GetComponent<PlayerStats>()
                          ?? playerObj.GetComponentInParent<PlayerStats>()
                          ?? playerObj.GetComponentInChildren<PlayerStats>();
        }

        if (player == null)
            Debug.LogWarning("[EnemyChaseAI] No object tagged 'Player' found — enemy will idle.");
        else if (playerStats == null)
            Debug.LogWarning("[EnemyChaseAI] Player found but no PlayerStats — enemy cannot deal damage.");
    }

    private void Update()
    {
        if (isDead || player == null) return;

        // The agent may not have settled onto the NavMesh yet (e.g. spawned a frame
        // before the bake finished, or dropped onto a gap) so skip until it is valid.
        if (agent == null || !agent.isOnNavMesh) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance > detectionRange)
        {
            // Idle — stop chasing player.
            agent.isStopped = true;
            return;
        }

        if (distance <= attackRange)
        {
            // In range to attack: hold position and attack on cooldown
            agent.isStopped = true;
            TryAttack();
        }
        else
        {
            // Chase the player
            agent.isStopped = false;
            agent.SetDestination(player.position);
        }
    }

    private void TryAttack()
    {
        if (playerStats == null) return;
        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        playerStats.Damage(attackDamage, false);
    }
}
